using Sangam.CelebrityVoting.Domain.Common;

namespace Sangam.CelebrityVoting.Domain.Campaigns;

/// <summary>
/// A "Celebrities of Samaaj" campaign: a nomination window, a voting window,
/// and a published result.
/// </summary>
/// <remarks>
/// <b>Votes are deliberately not part of this aggregate.</b> Every other
/// aggregate on the platform loads its children — a post loads its comments, an
/// event loads its registrations — and doing that here would be a mistake that
/// only shows up in production. A campaign in a large Samaaj has thousands of
/// votes; loading them to cast one more would read the whole table on the
/// platform's most contended write path, and at the close of voting that is
/// exactly when it must not.
///
/// So this aggregate holds the campaign and its candidates, which are bounded
/// and few. A <see cref="Vote"/> is written directly, its uniqueness enforced by
/// a database index rather than by anything held in memory, and the tally is a
/// GROUP BY rather than a count over a loaded collection. See
/// <c>IVoteRepository</c>.
/// </remarks>
public sealed class VotingCampaign : AggregateRoot, ITenantScopedEntity
{
    private readonly List<Candidate> _candidates = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }

    public DateTimeOffset NominationStartAt { get; private set; }
    public DateTimeOffset NominationEndAt { get; private set; }
    public DateTimeOffset VotingStartAt { get; private set; }
    public DateTimeOffset VotingEndAt { get; private set; }

    /// <summary>How many places the published result has.</summary>
    public int TopN { get; private set; }

    /// <summary>
    /// Whether the running tally is visible while voting is open.
    /// </summary>
    /// <remarks>
    /// <see cref="ResultsVisibility.HiddenUntilClose"/> exists because a live
    /// tally changes the election: members who can see who is winning vote
    /// differently from members who cannot. Which a Samaaj wants is theirs to
    /// decide, but it has to be decided before voting opens rather than
    /// discovered afterwards.
    /// </remarks>
    public ResultsVisibility ResultsVisibility { get; private set; }

    public CampaignStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyCollection<Candidate> Candidates => _candidates.AsReadOnly();

    private VotingCampaign() { }   // EF Core

    public static VotingCampaign Create(
        Guid tenantId,
        string title,
        string? description,
        DateTimeOffset nominationStartAt,
        DateTimeOffset nominationEndAt,
        DateTimeOffset votingStartAt,
        DateTimeOffset votingEndAt,
        int topN,
        ResultsVisibility resultsVisibility,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new VotingCampaign
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            NominationStartAt = nominationStartAt,
            NominationEndAt = nominationEndAt,
            VotingStartAt = votingStartAt,
            VotingEndAt = votingEndAt,
            TopN = topN,
            ResultsVisibility = resultsVisibility,
            Status = CampaignStatus.Draft,
            CreatedAt = now,
        };
    }

    public Candidate? FindCandidate(Guid candidateId) =>
        _candidates.FirstOrDefault(c => c.Id == candidateId);

    public Candidate? FindCandidateForMember(Guid memberId) =>
        _candidates.FirstOrDefault(c => c.MemberId == memberId);

    public bool HasCandidateFor(Guid memberId) =>
        _candidates.Any(c => c.MemberId == memberId);

    /// <summary>
    /// Whether nominations are open right now: the campaign says so, and the
    /// clock agrees.
    /// </summary>
    /// <remarks>
    /// Both, deliberately. The status is what an administrator moved it to; the
    /// window is what the Samaaj was told. A campaign left open past its end
    /// date because nobody clicked Close should still stop taking nominations.
    /// </remarks>
    public bool AcceptsNominations(DateTimeOffset now) =>
        Status == CampaignStatus.NominationsOpen
        && now >= NominationStartAt
        && now < NominationEndAt;

    public bool AcceptsVotes(DateTimeOffset now) =>
        Status == CampaignStatus.VotingOpen
        && now >= VotingStartAt
        && now < VotingEndAt;

    /// <summary>
    /// Whether the running tally may be shown to <paramref name="canAdminister"/>
    /// callers and ordinary members.
    /// </summary>
    /// <remarks>
    /// An administrator sees the tally whatever the setting: somebody has to be
    /// able to tell whether the thing is working. A member sees it only when the
    /// Samaaj chose a live count, or once it is over.
    /// </remarks>
    public bool TallyVisibleTo(bool canAdminister) =>
        canAdminister
        || ResultsVisibility == ResultsVisibility.Live
        || Status is CampaignStatus.Closed or CampaignStatus.Published;

    /// <summary>
    /// Moves the campaign on. Returns false when the move is not one this
    /// campaign can make.
    /// </summary>
    /// <remarks>
    /// A short, strictly forward sequence — Draft → NominationsOpen →
    /// VotingOpen → Closed → Published — so it is expressed as a next-state
    /// check rather than the transition table social-issues-service needs. An
    /// election that can go backwards is not an election.
    /// </remarks>
    public bool MoveTo(CampaignStatus target, DateTimeOffset now)
    {
        var allowed = Status switch
        {
            CampaignStatus.Draft => target == CampaignStatus.NominationsOpen,
            CampaignStatus.NominationsOpen => target == CampaignStatus.VotingOpen,
            CampaignStatus.VotingOpen => target == CampaignStatus.Closed,
            CampaignStatus.Closed => target == CampaignStatus.Published,
            _ => false,
        };

        if (!allowed)
        {
            return false;
        }

        var previous = Status;
        Status = target;

        if (target == CampaignStatus.Closed)
        {
            ClosedAt = now;
            Raise(new CampaignClosedDomainEvent(Id, TenantId, now));
        }

        if (target == CampaignStatus.Published)
        {
            PublishedAt = now;
        }

        Raise(new CampaignStatusChangedDomainEvent(
            Id, TenantId, previous.ToString(), target.ToString(), now));

        return true;
    }

    /// <summary>
    /// Puts somebody forward. Returns null when nominations are not open, or
    /// when this member has already been nominated.
    /// </summary>
    /// <remarks>
    /// One candidacy per member per campaign, however many people nominate
    /// them: two entries for the same person split their vote and make the
    /// result meaningless. A second nomination is a no-op rather than an error,
    /// because the second nominator has done nothing wrong.
    /// </remarks>
    public Candidate? Nominate(
        Guid memberId, string? category, Guid nominatedBy, DateTimeOffset now)
    {
        if (!AcceptsNominations(now) || HasCandidateFor(memberId))
        {
            return null;
        }

        var candidate = new Candidate(Id, memberId, category, nominatedBy, now);

        _candidates.Add(candidate);

        return candidate;
    }

    /// <summary>
    /// Approves a nomination, so it appears on the ballot. Returns false when
    /// there is no such candidate or it is already approved.
    /// </summary>
    /// <remarks>
    /// Nominations are approved rather than published straight to the ballot,
    /// for the same reason timeline posts are moderated: anyone can put
    /// anyone's name forward, and a Samaaj should not be made to hold a public
    /// vote about a person because one member typed their name.
    /// </remarks>
    public bool ApproveCandidate(Guid candidateId, Guid approvedBy, DateTimeOffset now)
    {
        var candidate = FindCandidate(candidateId);

        if (candidate is null || candidate.Status == CandidateStatus.Approved)
        {
            return false;
        }

        candidate.Approve(approvedBy, now);

        return true;
    }

    /// <summary>Removes a nomination before the ballot is set.</summary>
    public bool RejectCandidate(Guid candidateId, DateTimeOffset now)
    {
        if (Status is CampaignStatus.VotingOpen or CampaignStatus.Closed
            or CampaignStatus.Published)
        {
            // The ballot is set. Removing a candidate now would discard votes
            // already cast for them.
            return false;
        }

        var candidate = FindCandidate(candidateId);

        if (candidate is null)
        {
            return false;
        }

        _candidates.Remove(candidate);

        return true;
    }

    /// <summary>Candidates on the ballot: approved, in nomination order.</summary>
    public IReadOnlyList<Candidate> Ballot =>
        [.. _candidates
            .Where(c => c.Status == CandidateStatus.Approved)
            .OrderBy(c => c.NominatedAt)];

    /// <summary>Announces the ranked result. Raised by the handler, which has the tally.</summary>
    public void AnnounceResults(IReadOnlyList<Guid> rankedCandidateIds, DateTimeOffset now) =>
        Raise(new ResultsPublishedDomainEvent(Id, TenantId, rankedCandidateIds, now));
}

public enum CampaignStatus
{
    Draft = 1,
    NominationsOpen = 2,
    VotingOpen = 3,
    Closed = 4,
    Published = 5,
}

public enum ResultsVisibility
{
    /// <summary>The tally is visible while voting is open.</summary>
    Live = 1,

    /// <summary>
    /// Nobody but an administrator sees the tally until voting closes. A live
    /// count changes how people vote.
    /// </summary>
    HiddenUntilClose = 2,
}
