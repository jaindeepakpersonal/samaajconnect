namespace Sangam.VolunteerGroups.Domain.Elections;

/// <summary>
/// A group member put forward for a <see cref="GroupElection"/>'s position.
/// Owned by the election, mirroring celebrity-voting-service's own
/// <c>Candidate</c> shape.
/// </summary>
public sealed class ElectionCandidate
{
    public Guid Id { get; private set; }
    public Guid ElectionId { get; private set; }

    /// <summary>The group member being nominated. Names live in member-family-service.</summary>
    public Guid MemberId { get; private set; }

    public Guid NominatedBy { get; private set; }
    public DateTimeOffset NominatedAt { get; private set; }
    public CandidateStatus Status { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    private ElectionCandidate() { }   // EF Core

    internal ElectionCandidate(Guid electionId, Guid memberId, Guid nominatedBy, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ElectionId = electionId;
        MemberId = memberId;
        NominatedBy = nominatedBy;
        NominatedAt = now;
        Status = CandidateStatus.Nominated;
    }

    internal void Approve(Guid approvedBy, DateTimeOffset now)
    {
        Status = CandidateStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = now;
    }
}

public enum CandidateStatus
{
    /// <summary>Put forward, and not yet on the ballot.</summary>
    Nominated = 1,

    /// <summary>On the ballot.</summary>
    Approved = 2,
}

/// <summary>
/// One of a group member's picks in one election. A ballot is up to
/// <see cref="GroupElection.Seats"/> of these, written together.
/// </summary>
/// <remarks>
/// <b>Not part of the <see cref="GroupElection"/> aggregate</b>, for the same
/// reason celebrity-voting-service's own <c>Vote</c> is not part of
/// <c>VotingCampaign</c>: it is written directly, and database indexes - not a
/// check-then-insert in a handler, and not a distributed lock - are what
/// prevent a double vote. <see cref="Choice"/> is what lets them hold for a
/// multi-seat role too: unique on <c>(ElectionId, VoterMemberId, Choice)</c>,
/// a ballot's picks are numbered from 1, so a second ballot collides on its
/// first pick however it was built; and unique on <c>(ElectionId,
/// VoterMemberId, CandidateId)</c> nobody picks one candidate twice. Choice
/// numbers above the seats are refused by the handler, which knows them. See
/// <c>IVoteRepository</c>.
/// </remarks>
public sealed class Vote
{
    public Guid Id { get; private set; }
    public Guid ElectionId { get; private set; }
    public Guid CandidateId { get; private set; }
    public Guid VoterMemberId { get; private set; }

    /// <summary>1 to the election's seats; every ballot starts at 1. Not a ranking - every pick counts the same.</summary>
    public int Choice { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private Vote() { }   // EF Core

    public Vote(Guid electionId, Guid candidateId, Guid voterMemberId, int choice, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ElectionId = electionId;
        CandidateId = candidateId;
        VoterMemberId = voterMemberId;
        Choice = choice;
        CreatedAt = now;
    }
}
