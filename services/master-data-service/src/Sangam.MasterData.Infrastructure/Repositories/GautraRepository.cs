using Microsoft.EntityFrameworkCore;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Domain.Gautras;
using Sangam.MasterData.Infrastructure.Persistence;

namespace Sangam.MasterData.Infrastructure.Repositories;

public sealed class GautraRepository(MasterDataDbContext dbContext) : IGautraRepository
{
    /// <summary>
    /// Tenant-filtered by the DbContext's own global query filter - no
    /// explicit TenantId check needed here, unlike a raw SQL path.
    /// </summary>
    public Task<Gautra?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Gautras.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Gautra>> ListAsync(
        bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Gautras.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(g => g.IsActive);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(Gautra gautra) => dbContext.Gautras.Add(gautra);
}
