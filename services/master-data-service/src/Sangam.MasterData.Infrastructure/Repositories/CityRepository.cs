using Microsoft.EntityFrameworkCore;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Domain.Locations;
using Sangam.MasterData.Infrastructure.Persistence;

namespace Sangam.MasterData.Infrastructure.Repositories;

public sealed class CityRepository(MasterDataDbContext dbContext) : ICityRepository
{
    public Task<City?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Cities.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<City>> ListAsync(
        Guid? countryId, Guid? stateId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Cities.AsNoTracking();

        if (countryId is { } id)
        {
            query = query.Where(c => c.CountryId == id);
        }

        if (stateId is { } state)
        {
            query = query.Where(c => c.StateId == state);
        }

        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(City city) => dbContext.Cities.Add(city);
}
