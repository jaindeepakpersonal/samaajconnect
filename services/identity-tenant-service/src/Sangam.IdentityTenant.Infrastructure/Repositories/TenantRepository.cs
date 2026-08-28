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

    public void Add(Tenant tenant) => dbContext.Tenants.Add(tenant);
}
