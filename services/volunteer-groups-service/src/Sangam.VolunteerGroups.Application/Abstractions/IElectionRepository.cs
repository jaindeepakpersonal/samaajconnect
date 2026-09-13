using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Abstractions;

public interface IElectionRepository
{
    /// <summary>Tenant-filtered, with candidates loaded - and never votes. See <see cref="IVoteRepository"/>.</summary>
    Task<GroupElection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This group's elections, newest first.</summary>
    Task<IReadOnlyList<GroupElection>> ListForGroupAsync(
        Guid groupId, CancellationToken cancellationToken = default);

    void Add(GroupElection election);
}

/// <summary>
/// Votes, kept away from the election aggregate on purpose - see
/// <c>GroupElection</c>'s own remarks for why.
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
    /// celebrity-voting-service's own <c>IVoteRepository.TryCastAsync</c> for
    /// why this writes on its own connection rather than the request's.
    /// </summary>
    Task<bool> TryCastAsync(Vote vote, CancellationToken cancellationToken = default);

    /// <summary>The tally, as a GROUP BY rather than a count over loaded rows.</summary>
    Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid electionId, CancellationToken cancellationToken = default);
}
