using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Application.Abstractions;

public interface IElectionRepository
{
    /// <summary>Tenant-filtered, with candidates loaded - and never votes. See <see cref="IVoteRepository"/>.</summary>
    Task<GroupElection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This group's elections, newest first.</summary>
    Task<IReadOnlyList<GroupElection>> ListForGroupAsync(
        Guid groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this role has an election not yet published - a second one
    /// running beside it would let two ballots decide the same seats.
    /// </summary>
    Task<bool> HasUnpublishedForRoleAsync(
        Guid groupId, Guid roleId, Guid? exceptElectionId, CancellationToken cancellationToken = default);

    void Add(GroupElection election);
}

/// <summary>
/// Votes, kept away from the election aggregate on purpose - see
/// <c>GroupElection</c>'s own remarks for why.
/// </summary>
public interface IVoteRepository
{
    /// <summary>
    /// This member's ballot in this election - empty if they have not voted.
    /// A courtesy check for a decent answer, and <b>not</b> the guarantee: two
    /// requests can both pass it. The unique indexes on <c>Vote</c> are what
    /// actually prevent a second ballot.
    /// </summary>
    Task<IReadOnlyList<Vote>> FindForVoterAsync(
        Guid electionId, Guid voterMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts one voter's whole ballot directly, in one transaction on its
    /// own scope, returning false - and writing nothing - when a unique index
    /// refuses any pick because this member has already voted. See
    /// celebrity-voting-service's own <c>IVoteRepository.TryCastAsync</c> for
    /// why this writes on its own connection rather than the request's.
    /// </summary>
    Task<bool> TryCastAsync(IReadOnlyList<Vote> ballot, CancellationToken cancellationToken = default);

    /// <summary>The tally, as a GROUP BY rather than a count over loaded rows.</summary>
    Task<IReadOnlyDictionary<Guid, int>> TallyAsync(
        Guid electionId, CancellationToken cancellationToken = default);
}
