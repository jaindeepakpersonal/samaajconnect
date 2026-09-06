using Microsoft.EntityFrameworkCore;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Domain.Lookups;
using Sangam.MasterData.Infrastructure.Persistence;

namespace Sangam.MasterData.Infrastructure.Repositories;

public sealed class LookupItemRepository(MasterDataDbContext dbContext) : ILookupItemRepository
{
    public Task<LookupItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.LookupItems.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(
        LookupCategory category, string code, CancellationToken cancellationToken = default) =>
        dbContext.LookupItems.AnyAsync(
            i => i.Category == category && i.Code == code, cancellationToken);

    public async Task<IReadOnlyList<LookupItem>> ListAsync(
        LookupCategory category, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = dbContext.LookupItems.AsNoTracking().Where(i => i.Category == category);

        if (!includeInactive)
        {
            query = query.Where(i => i.IsActive);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(LookupItem item) => dbContext.LookupItems.Add(item);
}
