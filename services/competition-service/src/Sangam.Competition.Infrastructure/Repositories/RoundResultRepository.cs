using Microsoft.EntityFrameworkCore;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Domain.Entries;
using Sangam.Competition.Infrastructure.Persistence;

namespace Sangam.Competition.Infrastructure.Repositories;

public sealed class RoundResultRepository(CompetitionDbContext dbContext) : IRoundResultRepository
{
    public Task<RoundResult?> FindByRoundAsync(Guid roundId, CancellationToken cancellationToken = default) =>
        dbContext.RoundResults.AsNoTracking().FirstOrDefaultAsync(r => r.RoundId == roundId, cancellationToken);

    public void Add(RoundResult result) => dbContext.RoundResults.Add(result);
}
