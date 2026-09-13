using Microsoft.EntityFrameworkCore;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Domain.Entries;
using Sangam.Competition.Infrastructure.Persistence;

namespace Sangam.Competition.Infrastructure.Repositories;

public sealed class EntryRepository(CompetitionDbContext dbContext) : IEntryRepository
{
    public Task<Entry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Entries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<Entry?> FindForParticipantInRoundAsync(
        Guid roundId, Guid participantMemberId, CancellationToken cancellationToken = default) =>
        dbContext.Entries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.RoundId == roundId && e.ParticipantMemberId == participantMemberId,
                cancellationToken);

    public async Task<IReadOnlyList<Entry>> ListByRoundAsync(
        Guid roundId, CancellationToken cancellationToken = default) =>
        await dbContext.Entries
            .AsNoTracking()
            .Where(e => e.RoundId == roundId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountByRoundAsync(Guid roundId, CancellationToken cancellationToken = default) =>
        dbContext.Entries.AsNoTracking().CountAsync(e => e.RoundId == roundId, cancellationToken);

    public void Add(Entry entry) => dbContext.Entries.Add(entry);
}
