using Sangam.Trust.Domain.Elections;

namespace Sangam.Trust.Application.Abstractions;

public interface IElectionRepository
{
    /// <summary>Tenant-filtered, with candidates loaded - and never votes. See <see cref="IVoteRepository"/>.</summary>
    Task<TrustElection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This Trust's elections, newest first.</summary>
    Task<IReadOnlyList<TrustElection>> ListForTrustAsync(
        Guid trustId, CancellationToken cancellationToken = default);

    /// <summary>Whether any election for this role has not yet been published - a role being elected is not removed.</summary>
    Task<bool> AnyUnpublishedForRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    void Add(TrustElection election);
}

/// <summary>
/// Votes, kept away from the election aggregate on purpose - see
/// <c>TrustElection</c>'s own remarks for why.
/// </summary>
public interface IVoteRepository
{
    /// <summary>
    /// The candidates this member picked, in order of choice - empty when they
    /// have not voted. A courtesy check for a decent answer, and <b>not</b>
    /// the guarantee: two requests can both find nothing. The unique indexes
    /// are what actually prevent a second ballot.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindForVoterAsync(
        Guid electionId, Guid voterMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts one voter's whole ballot - one row per pick - on its own scope
    /// and in one transaction, returning false when a unique index refuses any
    /// of it because this member has already voted. Every ballot's first pick
    /// is choice 1, so a second ballot always collides on
    /// <c>(ElectionId, VoterMemberId, Choice)</c> and none of it lands. See
    /// volunteer-groups-service's own <c>VoteRepository.TryCastAsync</c> for
    /// why this writes on its own connection rather than the request's.
    /// </summary>
    Task<bool> TryCastAsync(IReadOnlyList<Vote> ballot, CancellationToken cancellationToken = default);

    /// <summary>The tally, as a GROUP BY rather than a count over loaded rows.</summary>
    Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid electionId, CancellationToken cancellationToken = default);
}
