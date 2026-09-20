namespace Sangam.Panch.Domain.Elections;

/// <summary>
/// Somebody put forward in an election. Owned by <see cref="PanchElection"/>.
/// </summary>
public sealed class Candidate
{
    public Guid Id { get; private set; }
    public Guid ElectionId { get; private set; }

    /// <summary>The member being nominated. Names live in member-family-service.</summary>
    public Guid MemberId { get; private set; }

    public Guid NominatedBy { get; private set; }
    public DateTimeOffset NominatedAt { get; private set; }
    public CandidateStatus Status { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    private Candidate() { }   // EF Core

    internal Candidate(Guid electionId, Guid memberId, Guid nominatedBy, DateTimeOffset now)
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
/// The fact that a member has voted in an election - one row per voter, and the
/// row the database uses to make a second ballot impossible.
/// </summary>
/// <remarks>
/// <b>The once-only guarantee is a unique index on
/// <c>(ElectionId, VoterMemberId)</c></b>, not a check-then-insert in a handler
/// and not a lock: two requests arriving in the same millisecond at the close
/// of voting is the normal case here, and an index is the only mechanism that
/// cannot be raced. A member's ballot is one submission of up to
/// <see cref="PanchElection.SeatCount"/> choices, written together with this
/// row in a single transaction, so "at most ten" is one request's validation
/// rather than a race between ten separate inserts.
/// </remarks>
public sealed class Ballot
{
    public Guid Id { get; private set; }
    public Guid ElectionId { get; private set; }
    public Guid VoterMemberId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Ballot() { }   // EF Core

    public Ballot(Guid electionId, Guid voterMemberId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ElectionId = electionId;
        VoterMemberId = voterMemberId;
        CreatedAt = now;
    }
}

/// <summary>
/// One choice on a <see cref="Ballot"/>: this voter backs this candidate. A voter
/// backs each candidate at most once (unique on election, candidate and voter).
/// </summary>
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

/// <summary>
/// The published outcome: an ordered list of candidate ids, frozen at the moment
/// of publication. Stored rather than recomputed - a result recomputed on every
/// read could change after it was announced.
/// </summary>
public sealed class ElectionResult
{
    public Guid Id { get; private set; }
    public Guid ElectionId { get; private set; }

    /// <summary>Ordered best-first, every ballot candidate. Stored as JSON; the order is the result.</summary>
    public IReadOnlyList<Guid> RankedCandidateIds { get; private set; } = [];

    /// <summary>The members who won seats, in rank order.</summary>
    public IReadOnlyList<Guid> SeatedMemberIds { get; private set; } = [];

    public Guid PublishedBy { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }

    private ElectionResult() { }   // EF Core

    public ElectionResult(
        Guid electionId,
        IReadOnlyList<Guid> rankedCandidateIds,
        IReadOnlyList<Guid> seatedMemberIds,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        Id = Guid.NewGuid();
        ElectionId = electionId;
        RankedCandidateIds = rankedCandidateIds;
        SeatedMemberIds = seatedMemberIds;
        PublishedBy = publishedBy;
        PublishedAt = publishedAt;
    }
}
