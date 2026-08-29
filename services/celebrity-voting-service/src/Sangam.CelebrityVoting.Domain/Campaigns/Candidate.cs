namespace Sangam.CelebrityVoting.Domain.Campaigns;

/// <summary>
/// Somebody put forward in a campaign. Owned by <see cref="VotingCampaign"/>.
/// </summary>
public sealed class Candidate
{
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }

    /// <summary>The member being nominated. Names live in member-family-service.</summary>
    public Guid MemberId { get; private set; }

    /// <summary>What they are nominated for. Free text, and optional.</summary>
    public string? Category { get; private set; }

    public Guid NominatedBy { get; private set; }
    public DateTimeOffset NominatedAt { get; private set; }
    public CandidateStatus Status { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    private Candidate() { }   // EF Core

    internal Candidate(
        Guid campaignId, Guid memberId, string? category, Guid nominatedBy, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CampaignId = campaignId;
        MemberId = memberId;
        Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
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
/// One member's vote.
/// </summary>
/// <remarks>
/// <b>Not part of the campaign aggregate, and that is the point.</b> A campaign
/// in a large Samaaj has thousands of these; loading them to cast one more
/// would read the whole table on the platform's most contended write path,
/// exactly when it must not.
///
/// A vote is written directly and its uniqueness is a database index on
/// <c>(CampaignId, VoterMemberId)</c>. That index — not a check-then-insert in
/// a handler, and not a distributed lock — is what actually prevents
/// double-voting, because it is the only mechanism that cannot be raced.
/// SERVICES.md calls this a correctness requirement rather than a
/// nice-to-have, and it is right: two requests arriving in the same
/// millisecond at the close of voting is the normal case here, not the edge.
/// </remarks>
public sealed class Vote
{
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid CandidateId { get; private set; }
    public Guid VoterMemberId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Vote() { }   // EF Core

    public Vote(Guid campaignId, Guid candidateId, Guid voterMemberId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CampaignId = campaignId;
        CandidateId = candidateId;
        VoterMemberId = voterMemberId;
        CreatedAt = now;
    }
}

/// <summary>
/// The published outcome: an ordered list of candidate ids, frozen at the
/// moment of publication.
/// </summary>
/// <remarks>
/// Stored rather than recomputed. A result recomputed on every read would be a
/// result that could change after it was announced — by a late vote, a
/// corrected one, or a candidate removed — and an announced result that moves is
/// worse than no result at all.
/// </remarks>
public sealed class CampaignResult
{
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }

    /// <summary>Ordered best-first. Stored as JSON; the order is the result.</summary>
    public IReadOnlyList<Guid> RankedCandidateIds { get; private set; } = [];

    public Guid PublishedBy { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }

    private CampaignResult() { }   // EF Core

    public CampaignResult(
        Guid campaignId,
        IReadOnlyList<Guid> rankedCandidateIds,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        Id = Guid.NewGuid();
        CampaignId = campaignId;
        RankedCandidateIds = rankedCandidateIds;
        PublishedBy = publishedBy;
        PublishedAt = publishedAt;
    }
}
