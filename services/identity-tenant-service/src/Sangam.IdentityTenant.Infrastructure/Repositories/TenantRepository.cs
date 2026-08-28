using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Repositories;

public sealed class TenantRepository(IdentityTenantDbContext dbContext) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tenant?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        dbContext.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default) =>
        dbContext.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken);

    public Task<bool> DomainExistsAsync(string domain, CancellationToken cancellationToken = default) =>
        dbContext.Tenants.AnyAsync(t => t.Domain == domain, cancellationToken);

    public async Task<IReadOnlyList<Tenant>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);


    public async Task<IReadOnlyList<Tenant>> ListAsync(
        TenantStatus? status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Tenants.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(t => t.Status == wanted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();

            // Name and slug only. A Samaaj's contact email is in this table and
            // matching on it would let an admin confirm an address they were
            // only guessing at - the same reason member search never touches
            // contact details.
            query = query.Where(t =>
                EF.Functions.ILike(t.Name, $"%{term}%")
                || EF.Functions.ILike(t.Slug, $"%{term}%"));
        }

        return await query.OrderBy(t => t.Name).ToListAsync(cancellationToken);
    }
    public void Add(Tenant tenant) => dbContext.Tenants.Add(tenant);
}
