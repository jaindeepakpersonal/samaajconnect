using Sangam.Panch.Domain.Elections;
using Sangam.Panch.Domain.Panel;

namespace Sangam.Panch.Application.Abstractions;

public interface IElectionRepository
{
    /// <summary>
    /// Tenant-filtered, with candidates loaded - and <b>never</b> ballots or
    /// votes, which are neither bounded nor few. See <see cref="IBallotRepository"/>.
    /// </summary>
    Task<PanchElection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PanchElection>> ListAsync(CancellationToken cancellationToken = default);

    void Add(PanchElection election);

    /// <summary>Deletes the election and, by cascade, its candidates.</summary>
    void Remove(PanchElection election);
}

/// <summary>
/// Ballots and their votes, kept away from the election aggregate on purpose.
/// </summary>
/// <remarks>
/// Every method is a targeted query or a single transactional insert. Nothing
/// loads an election's ballots into memory, because at the close of voting that
/// is exactly what would fall over.
/// </remarks>
public interface IBallotRepository
{
    /// <summary>
    /// The candidates this member backed, or null if they have not voted. A
    /// courtesy for a decent error message and for showing a member their own
    /// ballot - <b>not</b> the guarantee: two requests can both pass it. The
    /// unique index on (ElectionId, VoterMemberId) is what prevents a second ballot.
    /// </summary>
    Task<IReadOnlyList<Guid>?> FindChoicesAsync(
        Guid electionId, Guid voterMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a ballot and its votes in one transaction on their own scope,
    /// returning false when the unique index refuses because this member has
    /// already voted (nothing is written in that case).
    /// </summary>
    /// <remarks>
    /// Its own connection and transaction: holding a request-scoped transaction
    /// open across the contended path would serialise voters against each other,
    /// and a unique violation poisons the change tracker it happened on.
    /// Returning false rather than throwing, because pressing the button twice is
    /// the ordinary case.
    /// </remarks>
    Task<bool> TryCastAsync(
        Ballot ballot, IReadOnlyList<Vote> votes, CancellationToken cancellationToken = default);

    /// <summary>The tally, as a GROUP BY rather than a count over loaded rows.</summary>
    Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid electionId, CancellationToken cancellationToken = default);

    Task<ElectionResult?> FindResultAsync(
        Guid electionId, CancellationToken cancellationToken = default);

    void AddResult(ElectionResult result);
}

/// <summary>The seated panel: the roster rows for the current Samaaj.</summary>
public interface IPanelRepository
{
    /// <summary>Tracked, so a caller may remove them.</summary>
    Task<IReadOnlyList<PanelMember>> ListAsync(CancellationToken cancellationToken = default);

    void Add(PanelMember member);

    void Remove(PanelMember member);
}
