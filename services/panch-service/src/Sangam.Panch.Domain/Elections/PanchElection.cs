using Sangam.Panch.Domain.Common;

namespace Sangam.Panch.Domain.Elections;

/// <summary>
/// An election for the Samaaj Panch: a nomination window, a voting window, and
/// a published result that seats the winners.
/// </summary>
/// <remarks>
/// <b>Ballots are deliberately not part of this aggregate.</b> A Samaaj can
/// have thousands of voters and each ballot carries up to ten choices; loading
/// them to cast one more would read the whole table on the most contended
/// write path there is, at exactly the moment (the close of voting) it must
/// not. So this aggregate holds the election and its candidates, which are
/// bounded and few. A <see cref="Ballot"/> and its <see cref="Vote"/> rows are
/// written directly, their uniqueness enforced by database indexes, and the
/// tally is a GROUP BY. See <c>IBallotRepository</c>.
/// </remarks>
public sealed class PanchElection : AggregateRoot, ITenantScopedEntity
{
    /// <summary>The Samaaj Panch is ten members. Also the most a voter may back.</summary>
    public const int SeatCount = 10;

    private readonly List<Candidate> _candidates = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }

    public DateTimeOffset NominationStartAt { get; private set; }
    public DateTimeOffset NominationEndAt { get; private set; }
    public DateTimeOffset VotingStartAt { get; private set; }
    public DateTimeOffset VotingEndAt { get; private set; }

    /// <summary>
    /// Whether the running tally is visible while voting is open. A live count
    /// changes how people vote, so which a Samaaj wants has to be decided before
    /// voting opens rather than discovered afterwards.
    /// </summary>
    public ResultsVisibility ResultsVisibility { get; private set; }

    public ElectionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyCollection<Candidate> Candidates => _candidates.AsReadOnly();

    private PanchElection() { }   // EF Core

    public static PanchElection Create(
        Guid tenantId,
        string title,
        string? description,
        DateTimeOffset nominationStartAt,
        DateTimeOffset nominationEndAt,
        DateTimeOffset votingStartAt,
        DateTimeOffset votingEndAt,
        ResultsVisibility resultsVisibility,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new PanchElection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            NominationStartAt = nominationStartAt,
            NominationEndAt = nominationEndAt,
            VotingStartAt = votingStartAt,
            VotingEndAt = votingEndAt,
            ResultsVisibility = resultsVisibility,
            Status = ElectionStatus.Draft,
            CreatedAt = now,
        };
    }

    public Candidate? FindCandidate(Guid candidateId) =>
        _candidates.FirstOrDefault(c => c.Id == candidateId);

    public bool HasCandidateFor(Guid memberId) =>
        _candidates.Any(c => c.MemberId == memberId);

    /// <summary>
    /// Whether nominations are open right now: the election says so, and the
    /// clock agrees. Both, deliberately - a election left open past its end
    /// date because nobody clicked Close should still stop taking nominations.
    /// </summary>
    public bool AcceptsNominations(DateTimeOffset now) =>
        Status == ElectionStatus.NominationsOpen
        && now >= NominationStartAt
        && now < NominationEndAt;

    public bool AcceptsVotes(DateTimeOffset now) =>
        Status == ElectionStatus.VotingOpen
        && now >= VotingStartAt
        && now < VotingEndAt;

    /// <summary>
    /// Whether the running tally may be shown. An administrator sees it whatever
    /// the setting - somebody has to be able to tell whether the thing is
    /// working - and a member sees it only when the Samaaj chose a live count,
    /// or once voting is over.
    /// </summary>
    public bool TallyVisibleTo(bool canAdminister) =>
        canAdminister
        || ResultsVisibility == ResultsVisibility.Live
        || Status is ElectionStatus.Closed or ElectionStatus.Published;

    /// <summary>
    /// Moves the election on. Returns false when the move is not one this
    /// election can make. A short, strictly forward sequence - Draft,
    /// NominationsOpen, VotingOpen, Closed, Published - because an election that
    /// can go backwards is not an election.
    /// </summary>
    public bool MoveTo(ElectionStatus target, DateTimeOffset now)
    {
        var allowed = Status switch
        {
            ElectionStatus.Draft => target == ElectionStatus.NominationsOpen,
            ElectionStatus.NominationsOpen => target == ElectionStatus.VotingOpen,
            ElectionStatus.VotingOpen => target == ElectionStatus.Closed,
            ElectionStatus.Closed => target == ElectionStatus.Published,
            _ => false,
        };

        if (!allowed)
        {
            return false;
        }

        var previous = Status;
        Status = target;

        if (target == ElectionStatus.Closed)
        {
            ClosedAt = now;
        }

        if (target == ElectionStatus.Published)
        {
            PublishedAt = now;
        }

        Raise(new ElectionStatusChangedDomainEvent(
            Id, TenantId, previous.ToString(), target.ToString(), now));

        return true;
    }

    /// <summary>
    /// Puts somebody forward. Returns null when nominations are not open, or
    /// when this member has already been nominated: one candidacy per member,
    /// however many people nominate them, because two entries for one person
    /// split their votes. A second nomination is a no-op, not an error.
    /// </summary>
    public Candidate? Nominate(Guid memberId, Guid nominatedBy, DateTimeOffset now)
    {
        if (!AcceptsNominations(now) || HasCandidateFor(memberId))
        {
            return null;
        }

        var candidate = new Candidate(Id, memberId, nominatedBy, now);

        _candidates.Add(candidate);

        return candidate;
    }

    /// <summary>
    /// Approves a nomination, so it is on the ballot. Anyone can put anyone's
    /// name forward, and a Samaaj should not hold a vote about a person because
    /// one member typed their name.
    /// </summary>
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
        if (Status is ElectionStatus.VotingOpen or ElectionStatus.Closed or ElectionStatus.Published)
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

    /// <summary>
    /// The candidates who win seats: ranked by votes, best first, ties broken on
    /// nomination order, at most <see cref="SeatCount"/>, and each with at least
    /// one vote - a seat nobody voted for is not an election result.
    /// </summary>
    public IReadOnlyList<Candidate> Winners(IReadOnlyDictionary<Guid, int> tally) =>
        [.. Ballot
            .Where(c => tally.GetValueOrDefault(c.Id) > 0)
            .OrderByDescending(c => tally.GetValueOrDefault(c.Id))
            .ThenBy(c => c.NominatedAt)
            .Take(SeatCount)];

    /// <summary>Announces the panel this election seated. Raised by the handler, which has the tally.</summary>
    public void AnnouncePanel(IReadOnlyList<Guid> seatedMemberIds, Guid publishedBy, DateTimeOffset now) =>
        Raise(new PanelChangedDomainEvent(TenantId, seatedMemberIds, "Elected", Id, publishedBy, now));

    /// <summary>Every ballot candidate, ranked as <see cref="Winners"/> ranks them.</summary>
    public IReadOnlyList<Candidate> RankBy(IReadOnlyDictionary<Guid, int> tally) =>
        [.. Ballot
            .OrderByDescending(c => tally.GetValueOrDefault(c.Id))
            .ThenBy(c => c.NominatedAt)];
}

public enum ElectionStatus
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

    /// <summary>Nobody but an administrator sees the tally until voting closes.</summary>
    HiddenUntilClose = 2,
}
