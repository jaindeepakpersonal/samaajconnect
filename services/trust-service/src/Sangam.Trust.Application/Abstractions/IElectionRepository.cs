using Sangam.Trust.Domain.Elections;

namespace Sangam.Trust.Application.Abstractions;

public interface IElectionRepository
{
    /// <summary>Tenant-filtered, with candidates loaded - and never votes. See <see cref="IVoteRepository"/>.</summary>
    Task<TrustElection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This Trust's elections, newest first.</summary>
    Task<IReadOnlyList<TrustElection>> ListForTrustAsync(
        Guid trustId, CancellationToken cancellationToken = default);

    void Add(TrustElection election);
}

/// <summary>
/// Votes, kept away from the election aggregate on purpose - see
/// <c>TrustElection</c>'s own remarks for why.
/// </summary>
public interface IVoteRepository
{
    /// <summary>
    /// Has this member already voted in this election? A courtesy check for a
    /// decent error message, and <b>not</b> the guarantee: two requests can
    /// both pass it. The unique index on (ElectionId, VoterMemberId) is what
    /// actually prevents a double vote.
    /// </summary>
    Task<Vote?> FindForVoterAsync(
        Guid electionId, Guid voterMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a vote directly, on its own scope, returning false when the
    /// unique index refuses it because this member has already voted. See
    /// volunteer-groups-service's own <c>VoteRepository.TryCastAsync</c> for
    /// why this writes on its own connection rather than the request's.
    /// </summary>
    Task<bool> TryCastAsync(Vote vote, CancellationToken cancellationToken = default);

    /// <summary>The tally, as a GROUP BY rather than a count over loaded rows.</summary>
    Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid electionId, CancellationToken cancellationToken = default);
}
