using Microsoft.EntityFrameworkCore;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Domain.Locations;
using Sangam.MasterData.Infrastructure.Persistence;

namespace Sangam.MasterData.Infrastructure.Repositories;

public sealed class LocalityRepository(MasterDataDbContext dbContext) : ILocalityRepository
{
    public Task<Locality?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Localities.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Locality>> ListAsync(
        Guid? cityId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Localities.AsNoTracking();

        if (cityId is { } id)
        {
            query = query.Where(l => l.CityId == id);
        }

        if (!includeInactive)
        {
            query = query.Where(l => l.IsActive);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(Locality locality) => dbContext.Localities.Add(locality);
}
