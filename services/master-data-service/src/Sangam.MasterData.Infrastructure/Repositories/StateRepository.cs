using Microsoft.EntityFrameworkCore;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Domain.Locations;
using Sangam.MasterData.Infrastructure.Persistence;

namespace Sangam.MasterData.Infrastructure.Repositories;

public sealed class StateRepository(MasterDataDbContext dbContext) : IStateRepository
{
    public Task<State?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.States.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<State>> ListAsync(
        Guid? countryId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        var query = dbContext.States.AsNoTracking();

        if (countryId is { } id)
        {
            query = query.Where(s => s.CountryId == id);
        }

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public void Add(State state) => dbContext.States.Add(state);
}
