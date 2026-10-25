using Microsoft.EntityFrameworkCore;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Domain.Entries;
using Sangam.Competition.Infrastructure.Persistence;

namespace Sangam.Competition.Infrastructure.Repositories;

public sealed class CompetitionResultRepository(CompetitionDbContext dbContext) : ICompetitionResultRepository
{
    public Task<CompetitionResult?> FindByCompetitionAsync(
        Guid competitionId, CancellationToken cancellationToken = default) =>
        dbContext.CompetitionResults
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CompetitionId == competitionId, cancellationToken);

    public void Add(CompetitionResult result) => dbContext.CompetitionResults.Add(result);
}
