using Sangam.VolunteerGroups.Domain.Common;
using Sangam.VolunteerGroups.Domain.Elections.Events;

namespace Sangam.VolunteerGroups.Domain.Elections;

/// <summary>
/// An election for one volunteer role within one group: a nomination window,
/// a voting window, and - once closed - as many winners as the role has seats.
/// </summary>
/// <remarks>
/// Copies the shape of celebrity-voting-service's <c>VotingCampaign</c>
/// rather than sharing code with it: that service runs one platform-wide
/// award with a ranked top-N result; this one runs one group's one position
/// with exactly one winner, and the two are different enough shapes that
/// coupling them would cost more than the duplication saves.
///
/// <b>Votes are deliberately not part of this aggregate</b>, for the same
/// reason <c>VotingCampaign</c>'s own votes are not: a <see cref="Vote"/> is
/// written directly, its uniqueness enforced by a database index rather than
/// anything held in memory, and the tally is a GROUP BY rather than a count
/// over a loaded collection. A volunteer group is smaller than a Samaaj, but
/// the shape of the mistake - loading every vote to cast one more - is the
/// same mistake at a smaller scale, and there is no reason to make it just
/// because the size makes it survivable today.
/// </remarks>
public sealed class GroupElection : AggregateRoot, ITenantScopedEntity
{
    private readonly List<ElectionCandidate> _candidates = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>The group this election fills a position for. A plain reference - see CLAUDE.md.</summary>
    public Guid GroupId { get; private set; }

    /// <summary>The group's <c>GroupRole</c> this election fills. A plain reference, within one group.</summary>
    public Guid RoleId { get; private set; }

    /// <summary>
    /// The role's title when the election was opened. Kept on the election so
    /// its history still reads correctly after the role is renamed or removed.
    /// </summary>
    public string PositionTitle { get; private set; } = null!;

    /// <summary>
    /// How many winners it produces, and how many candidates each voter may
    /// pick - the role's seats when the election was set up, frozen here so a
    /// role resized mid-vote does not change the ballot under anybody.
    /// </summary>
    public int Seats { get; private set; }

    public DateTimeOffset NominationStartAt { get; private set; }
    public DateTimeOffset NominationEndAt { get; private set; }
    public DateTimeOffset VotingStartAt { get; private set; }
    public DateTimeOffset VotingEndAt { get; private set; }

    public ElectionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Set once, by <see cref="Publish"/>, in ranked order. Empty until then.</summary>
    public List<Guid> WinnerMemberIds { get; private set; } = [];

    public DateTimeOffset? PublishedAt { get; private set; }

    public IReadOnlyCollection<ElectionCandidate> Candidates => _candidates.AsReadOnly();

    private GroupElection() { }   // EF Core

    public static GroupElection Open(
        Guid tenantId,
        Guid groupId,
        Guid roleId,
        string positionTitle,
        int seats,
        DateTimeOffset nominationStartAt,
        DateTimeOffset nominationEndAt,
        DateTimeOffset votingStartAt,
        DateTimeOffset votingEndAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionTitle);
        ArgumentOutOfRangeException.ThrowIfLessThan(seats, 1);

        return new GroupElection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            GroupId = groupId,
            RoleId = roleId,
            PositionTitle = positionTitle.Trim(),
            Seats = seats,
            NominationStartAt = nominationStartAt,
            NominationEndAt = nominationEndAt,
            VotingStartAt = votingStartAt,
            VotingEndAt = votingEndAt,
            Status = ElectionStatus.Draft,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Whether the president (or the admin running a president-less group's own
    /// President election) can still correct this election's details.
    /// </summary>
    /// <remarks>
    /// Draft or NominationsOpen only - the identical rule and the identical
    /// reasoning celebrity-voting-service's own <c>VotingCampaign.CanBeEdited</c>
    /// uses. Once voting has opened, group members have been told the windows
    /// and may already have voted against them; changing the ballot's timing
    /// out from under them is a bigger act than a correction.
    /// </remarks>
    public bool CanBeEdited => Status is ElectionStatus.Draft or ElectionStatus.NominationsOpen;

    /// <summary>
    /// Replaces the election's position title and windows - most often to push
    /// <see cref="NominationEndAt"/> out when nominations closed with too few
    /// names on the ballot. The caller has already checked <see cref="CanBeEdited"/>
    /// and the window ordering; this only does the write.
    /// </summary>
    /// <remarks>
    /// Not a way around the "strictly forward" rule on <see cref="Status"/>:
    /// this never moves <see cref="Status"/> itself. An election left at
    /// NominationsOpen past its own <see cref="NominationEndAt"/> has
    /// <see cref="AcceptsNominations"/> already false with nothing to undo - it
    /// never advanced to VotingOpen, so extending the date here is enough to
    /// reopen it. An election already moved to VotingOpen or beyond is refused
    /// by <see cref="CanBeEdited"/> before this runs.
    /// </remarks>
    public void Update(
        Guid roleId,
        string positionTitle,
        int seats,
        DateTimeOffset nominationStartAt,
        DateTimeOffset nominationEndAt,
        DateTimeOffset votingStartAt,
        DateTimeOffset votingEndAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(positionTitle);
        ArgumentOutOfRangeException.ThrowIfLessThan(seats, 1);

        RoleId = roleId;
        PositionTitle = positionTitle.Trim();
        Seats = seats;
        NominationStartAt = nominationStartAt;
        NominationEndAt = nominationEndAt;
        VotingStartAt = votingStartAt;
        VotingEndAt = votingEndAt;

        Raise(new GroupElectionUpdatedDomainEvent(Id, TenantId, GroupId, now));
    }

    public ElectionCandidate? FindCandidate(Guid candidateId) =>
        _candidates.FirstOrDefault(c => c.Id == candidateId);

    public ElectionCandidate? FindCandidateForMember(Guid memberId) =>
        _candidates.FirstOrDefault(c => c.MemberId == memberId);

    public bool HasCandidateFor(Guid memberId) => _candidates.Any(c => c.MemberId == memberId);

    /// <summary>Nominations are open now: the election says so, and the clock agrees.</summary>
    public bool AcceptsNominations(DateTimeOffset now) =>
        Status == ElectionStatus.NominationsOpen
        && now >= NominationStartAt
        && now < NominationEndAt;

    public bool AcceptsVotes(DateTimeOffset now) =>
        Status == ElectionStatus.VotingOpen
        && now >= VotingStartAt
        && now < VotingEndAt;

    /// <summary>
    /// Moves the election on. Returns false when the move is not one this
    /// election can make.
    /// </summary>
    /// <remarks>
    /// Strictly forward - Draft -> NominationsOpen -> VotingOpen -> Closed ->
    /// Published - the identical sequence and the identical reasoning
    /// <c>VotingCampaign.MoveTo</c> already uses: an election that can go
    /// backwards is not an election.
    /// </remarks>
    public bool MoveTo(ElectionStatus target, DateTimeOffset now)
    {
        var allowed = Status switch
        {
            ElectionStatus.Draft => target == ElectionStatus.NominationsOpen,
            ElectionStatus.NominationsOpen => target == ElectionStatus.VotingOpen,
            ElectionStatus.VotingOpen => target == ElectionStatus.Closed,
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
            Raise(new GroupElectionClosedDomainEvent(Id, TenantId, GroupId, now));
        }

        Raise(new GroupElectionStatusChangedDomainEvent(
            Id, TenantId, GroupId, previous.ToString(), target.ToString(), now));

        return true;
    }

    /// <summary>
    /// Puts a group member forward. Returns null when nominations are not
    /// open, or when this member already has a candidacy.
    /// </summary>
    /// <remarks>
    /// Whether <paramref name="memberId"/> is actually a member of this
    /// election's group is not this aggregate's to check - it does not hold
    /// the group's own member list - so the handler checks it against the
    /// loaded <c>VolunteerGroup</c> before calling this.
    /// </remarks>
    public ElectionCandidate? Nominate(Guid memberId, Guid nominatedBy, DateTimeOffset now)
    {
        if (!AcceptsNominations(now) || HasCandidateFor(memberId))
        {
            return null;
        }

        var candidate = new ElectionCandidate(Id, memberId, nominatedBy, now);

        _candidates.Add(candidate);

        return candidate;
    }

    /// <summary>Approves a nomination onto the ballot. False when there is none, or it already is.</summary>
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
    public IReadOnlyList<ElectionCandidate> Ballot =>
        [.. _candidates
            .Where(c => c.Status == CandidateStatus.Approved)
            .OrderBy(c => c.NominatedAt)];

    /// <summary>
    /// The top <see cref="Seats"/> candidates by tally, tie-broken by earliest
    /// nomination - the identical tie-break <c>VotingCampaign.RankBy</c>
    /// already uses, held by the same two mechanisms agreeing: this ordering,
    /// and the stable sort LINQ's own <c>OrderBy</c> already performs. Fewer
    /// than <see cref="Seats"/> when the ballot is shorter: every name on it
    /// wins, and the rest of the seats stay empty.
    /// </summary>
    public IReadOnlyList<ElectionCandidate> WinnersBy(IReadOnlyDictionary<Guid, int> tally) =>
        [.. Ballot
            .OrderByDescending(c => tally.GetValueOrDefault(c.Id))
            .ThenBy(c => c.NominatedAt)
            .Take(Seats)];

    /// <summary>
    /// Freezes the winners and moves to Published. Only from Closed. Returns
    /// false on a second attempt - publishing twice would leave "the result"
    /// with no single referent.
    /// </summary>
    public bool Publish(IReadOnlyList<Guid> winnerMemberIds, DateTimeOffset now)
    {
        if (Status != ElectionStatus.Closed || winnerMemberIds.Count == 0)
        {
            return false;
        }

        Status = ElectionStatus.Published;
        WinnerMemberIds = [.. winnerMemberIds];
        PublishedAt = now;

        Raise(new GroupElectionResultsPublishedDomainEvent(
            Id, TenantId, GroupId, winnerMemberIds[0], now, [.. winnerMemberIds]));

        return true;
    }
}

public enum ElectionStatus
{
    Draft = 1,
    NominationsOpen = 2,
    VotingOpen = 3,
    Closed = 4,
    Published = 5,
}
