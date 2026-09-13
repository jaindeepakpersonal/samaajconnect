using Microsoft.EntityFrameworkCore;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Infrastructure.Persistence;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.Infrastructure.Repositories;

public sealed class CompetitionRepository(CompetitionDbContext dbContext) : ICompetitionRepository
{
    public Task<CompetitionAggregate?> GetByIdAsync(
        Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Competitions
            // Registrations, judges and rounds - all bounded and few. Never
            // entries or scores; see IEntryRepository/IScoreRepository.
            .Include(c => c.Registrations)
            .Include(c => c.Judges)
            .Include(c => c.Rounds)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CompetitionAggregate>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Competitions
            .AsNoTracking()
            .Include(c => c.Registrations)
            .Include(c => c.Judges)
            .Include(c => c.Rounds)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(CompetitionAggregate competition) => dbContext.Competitions.Add(competition);
}
