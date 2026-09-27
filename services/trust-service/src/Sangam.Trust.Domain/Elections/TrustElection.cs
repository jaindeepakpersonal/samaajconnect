using Sangam.Trust.Domain.Common;
using Sangam.Trust.Domain.Elections.Events;

namespace Sangam.Trust.Domain.Elections;

/// <summary>
/// An election for one named position on one Trust's roster: a nomination
/// window, a voting window, and - once closed - a single winner.
/// </summary>
/// <remarks>
/// Copies volunteer-groups-service's <c>GroupElection</c> shape rather than
/// sharing code with it - that service's own CLAUDE.md documents the choice:
/// different enough bounded contexts that coupling them would cost more than
/// the duplication saves. Election-for-a-single-winner has now been copied a
/// third time (celebrity-voting-service's ranked <c>VotingCampaign</c> was
/// the original; <c>GroupElection</c> the second), and each copy keeps its
/// own shape rather than reaching for a shared base the three do not
/// actually need in common.
///
/// <b>Votes are deliberately not part of this aggregate</b>, for the
/// identical reason <c>GroupElection</c>'s own are not: a <see cref="Vote"/>
/// is written directly, its uniqueness enforced by a database index, and the
/// tally is a GROUP BY rather than a count over a loaded collection.
/// </remarks>
public sealed class TrustElection : AggregateRoot, ITenantScopedEntity
{
    private readonly List<TrustElectionCandidate> _candidates = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>The Trust this election fills a position for. A plain reference - see CLAUDE.md.</summary>
    public Guid TrustId { get; private set; }

    /// <summary>The role it fills. Never the built-in Trustee role.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>How many it fills - the role's seats when it was set up, and a ballot's most picks.</summary>
    public int Seats { get; private set; }

    public DateTimeOffset NominationStartAt { get; private set; }
    public DateTimeOffset NominationEndAt { get; private set; }
    public DateTimeOffset VotingStartAt { get; private set; }
    public DateTimeOffset VotingEndAt { get; private set; }

    public TrustElectionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Set once, by <see cref="Publish"/>, in ranked order. Empty until then.</summary>
    public List<Guid> WinnerMemberIds { get; private set; } = [];

    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyCollection<TrustElectionCandidate> Candidates => _candidates.AsReadOnly();

    private TrustElection() { }   // EF Core

    public static TrustElection Open(
        Guid tenantId,
        Guid trustId,
        Guid roleId,
        int seats,
        DateTimeOffset nominationStartAt,
        DateTimeOffset nominationEndAt,
        DateTimeOffset votingStartAt,
        DateTimeOffset votingEndAt,
        DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seats, 1);

        return new TrustElection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TrustId = trustId,
            RoleId = roleId,
            Seats = seats,
            NominationStartAt = nominationStartAt,
            NominationEndAt = nominationEndAt,
            VotingStartAt = votingStartAt,
            VotingEndAt = votingEndAt,
            Status = TrustElectionStatus.Draft,
            CreatedAt = now,
        };
    }

    public TrustElectionCandidate? FindCandidate(Guid candidateId) =>
        _candidates.FirstOrDefault(c => c.Id == candidateId);

    public TrustElectionCandidate? FindCandidateForMember(Guid memberId) =>
        _candidates.FirstOrDefault(c => c.MemberId == memberId);

    public bool HasCandidateFor(Guid memberId) => _candidates.Any(c => c.MemberId == memberId);

    /// <summary>Nominations are open now: the election says so, and the clock agrees.</summary>
    public bool AcceptsNominations(DateTimeOffset now) =>
        Status == TrustElectionStatus.NominationsOpen
        && now >= NominationStartAt
        && now < NominationEndAt;

    public bool AcceptsVotes(DateTimeOffset now) =>
        Status == TrustElectionStatus.VotingOpen
        && now >= VotingStartAt
        && now < VotingEndAt;

    /// <summary>
    /// Moves the election on. Returns false when the move is not one this
    /// election can make.
    /// </summary>
    /// <remarks>
    /// Strictly forward - Draft -> NominationsOpen -> VotingOpen -> Closed ->
    /// Published - the identical sequence <c>GroupElection.MoveTo</c> already
    /// uses: an election that can go backwards is not an election.
    /// </remarks>
    public bool MoveTo(TrustElectionStatus target, DateTimeOffset now)
    {
        var allowed = Status switch
        {
            TrustElectionStatus.Draft => target == TrustElectionStatus.NominationsOpen,
            TrustElectionStatus.NominationsOpen => target == TrustElectionStatus.VotingOpen,
            TrustElectionStatus.VotingOpen => target == TrustElectionStatus.Closed,
            _ => false,
        };

        if (!allowed)
        {
            return false;
        }

        var previous = Status;
        Status = target;

        if (target == TrustElectionStatus.Closed)
        {
            ClosedAt = now;
        }

        Raise(new TrustElectionStatusChangedDomainEvent(
            Id, TenantId, TrustId, previous.ToString(), target.ToString(), now));

        return true;
    }

    /// <summary>
    /// Puts a Trust member forward. Returns null when nominations are not
    /// open, or when this member already has a candidacy.
    /// </summary>
    /// <remarks>
    /// Whether <paramref name="memberId"/> is actually an active member of
    /// this election's Trust is not this aggregate's to check - it does not
    /// hold the Trust's own roster, which lives in the separate
    /// <c>TrustMember</c> aggregate - so the handler checks it first.
    /// </remarks>
    public TrustElectionCandidate? Nominate(Guid memberId, Guid nominatedBy, DateTimeOffset now)
    {
        if (!AcceptsNominations(now) || HasCandidateFor(memberId))
        {
            return null;
        }

        var candidate = new TrustElectionCandidate(Id, memberId, nominatedBy, now);

        _candidates.Add(candidate);

        return candidate;
    }

    /// <summary>Approves a nomination onto the ballot. False when there is none, or it already is.</summary>
    public bool ApproveCandidate(Guid candidateId, Guid approvedBy, DateTimeOffset now)
    {
        var candidate = FindCandidate(candidateId);

        if (candidate is null || candidate.Status == TrustCandidateStatus.Approved)
        {
            return false;
        }

        candidate.Approve(approvedBy, now);

        return true;
    }

    /// <summary>Removes a nomination before the ballot is set.</summary>
    public bool RejectCandidate(Guid candidateId, DateTimeOffset now)
    {
        if (Status is TrustElectionStatus.VotingOpen or TrustElectionStatus.Closed or TrustElectionStatus.Published)
        {
            // The ballot is set. Removing now would discard votes already cast.
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
    public IReadOnlyList<TrustElectionCandidate> Ballot =>
        [.. _candidates
            .Where(c => c.Status == TrustCandidateStatus.Approved)
            .OrderBy(c => c.NominatedAt)];

    /// <summary>
    /// The top <see cref="Seats"/> candidates by tally, ties broken by the
    /// earlier nomination - the tie-break volunteer-groups-service's elections
    /// use. Fewer when the ballot is shorter: everybody on it wins.
    /// </summary>
    public IReadOnlyList<TrustElectionCandidate> WinnersBy(IReadOnlyDictionary<Guid, int> tally) =>
        [.. Ballot
            .OrderByDescending(c => tally.GetValueOrDefault(c.Id))
            .ThenBy(c => c.NominatedAt)
            .Take(Seats)];

    /// <summary>
    /// Freezes the winners and moves to Published. Only from Closed, and false
    /// on a second attempt - publishing twice would leave "the result" with no
    /// single referent.
    /// </summary>
    public bool Publish(IReadOnlyList<Guid> winnerMemberIds, DateTimeOffset now)
    {
        if (Status != TrustElectionStatus.Closed || winnerMemberIds.Count == 0)
        {
            return false;
        }

        Status = TrustElectionStatus.Published;
        WinnerMemberIds = [.. winnerMemberIds];
        PublishedAt = now;

        Raise(new TrustElectionResultsPublishedDomainEvent(Id, TenantId, TrustId, RoleId, [.. winnerMemberIds], now));

        return true;
    }
}

public enum TrustElectionStatus
{
    Draft = 1,
    NominationsOpen = 2,
    VotingOpen = 3,
    Closed = 4,
    Published = 5,
}
