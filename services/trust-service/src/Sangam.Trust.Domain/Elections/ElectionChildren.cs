namespace Sangam.Trust.Domain.Elections;

/// <summary>
/// A Trust member put forward for a <see cref="TrustElection"/>'s position.
/// Owned by the election, mirroring volunteer-groups-service's own
/// <c>ElectionCandidate</c> shape.
/// </summary>
public sealed class TrustElectionCandidate
{
    public Guid Id { get; private set; }
    public Guid ElectionId { get; private set; }

    /// <summary>The Trust member being nominated. Names live in member-family-service.</summary>
    public Guid MemberId { get; private set; }

    public Guid NominatedBy { get; private set; }
    public DateTimeOffset NominatedAt { get; private set; }
    public TrustCandidateStatus Status { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    private TrustElectionCandidate() { }   // EF Core

    internal TrustElectionCandidate(Guid electionId, Guid memberId, Guid nominatedBy, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ElectionId = electionId;
        MemberId = memberId;
        NominatedBy = nominatedBy;
        NominatedAt = now;
        Status = TrustCandidateStatus.Nominated;
    }

    internal void Approve(Guid approvedBy, DateTimeOffset now)
    {
        Status = TrustCandidateStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = now;
    }
}

public enum TrustCandidateStatus
{
    /// <summary>Put forward, and not yet on the ballot.</summary>
    Nominated = 1,

    /// <summary>On the ballot.</summary>
    Approved = 2,
}

/// <summary>
/// One Trust member's one vote in one election.
/// </summary>
/// <remarks>
/// <b>Not part of the <see cref="TrustElection"/> aggregate</b>, for the same
/// reason volunteer-groups-service's own <c>Vote</c> is not part of
/// <c>GroupElection</c>: it is written directly, and the unique index on
/// <c>(ElectionId, VoterMemberId)</c> - not a check-then-insert in a handler -
/// is what actually prevents a double vote. See <c>IVoteRepository</c>.
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
