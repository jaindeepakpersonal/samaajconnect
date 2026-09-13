using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Application.Abstractions;

/// <summary>
/// Entries, kept away from any owning aggregate on purpose - see
/// <c>Entry</c>'s own remarks. Every method here is a targeted query, an
/// insert, or a count; nothing loads a whole round's entries except the one
/// method built for exactly that.
/// </summary>
public interface IEntryRepository
{
    Task<Entry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Entry?> FindForParticipantInRoundAsync(
        Guid roundId, Guid participantMemberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Entry>> ListByRoundAsync(Guid roundId, CancellationToken cancellationToken = default);

    Task<int> CountByRoundAsync(Guid roundId, CancellationToken cancellationToken = default);

    void Add(Entry entry);
}
