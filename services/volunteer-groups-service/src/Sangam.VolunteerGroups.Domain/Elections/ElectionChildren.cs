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
/// One group member's one vote in one election.
/// </summary>
/// <remarks>
/// <b>Not part of the <see cref="GroupElection"/> aggregate</b>, for the same
/// reason celebrity-voting-service's own <c>Vote</c> is not part of
/// <c>VotingCampaign</c>: it is written directly, and the unique index on
/// <c>(ElectionId, VoterMemberId)</c> - not a check-then-insert in a handler,
/// and not a distributed lock - is what actually prevents a double vote. See
/// <c>IVoteRepository</c>.
/// </remarks>
public sealed class Vote
{
    public Guid Id { get; private set; }
    public Guid ElectionId { get; private set; }
    public Guid CandidateId { get; private set; }
    public Guid VoterMemberId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Vote() { }   // EF Core

    public Vote(Guid electionId, Guid candidateId, Guid voterMemberId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ElectionId = electionId;
        CandidateId = candidateId;
        VoterMemberId = voterMemberId;
        CreatedAt = now;
    }
}
