using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Infrastructure.Persistence;

namespace Sangam.Trust.Infrastructure.Repositories;

// Trust is referenced as `Domain.Trusts.Trust`, never bare - see
// ITrustRepository's own remarks.

public sealed class TrustRepository(
    TrustDbContext dbContext,
    IServiceScopeFactory scopeFactory)
    : ITrustRepository
{
    /// <summary>Postgres unique-violation SQLSTATE.</summary>
    private const string UniqueViolation = "23505";

    public Task<Domain.Trusts.Trust?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Trusts.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Domain.Trusts.Trust?> FindByLocalityAsync(
        Guid localityId, CancellationToken cancellationToken = default) =>
        dbContext.Trusts.AsNoTracking().FirstOrDefaultAsync(t => t.LocalityId == localityId, cancellationToken);

    public Task<Domain.Trusts.Trust?> FindByLocalityAsync(
        Guid tenantId, Guid localityId, CancellationToken cancellationToken = default) =>
        dbContext.Trusts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.LocalityId == localityId, cancellationToken);

    public async Task<IReadOnlyList<Domain.Trusts.Trust>> ListAsync(
        Guid? localityId, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Trusts.AsNoTracking().AsQueryable();

        if (localityId is { } id)
        {
            query = query.Where(t => t.LocalityId == id);
        }

        return await query.OrderByDescending(t => t.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<bool> TryAddAsync(Domain.Trusts.Trust trust, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TrustDbContext>();

        context.Trusts.Add(trust);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // The index refused it: this Samaaj already has a Trust for this
            // locality. That is the guarantee working.
            return false;
        }
    }
}
