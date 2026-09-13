using Sangam.Competition.Domain.Common;
using Sangam.Competition.Domain.Competitions.Events;

namespace Sangam.Competition.Domain.Competitions;

/// <summary>
/// A Samaaj competition: a registration window, zero or more judged rounds,
/// and the judges assigned to score them.
/// </summary>
/// <remarks>
/// Registrations and judges are bounded and few - this Samaaj's own members,
/// at most a few hundred - so they are owned collections here, the same
/// shape <c>VotingCampaign</c> owns its (also bounded) candidates. Rounds are
/// owned too, for the same reason.
///
/// <see cref="Entries.Entry"/> and <see cref="Entries.Score"/> are
/// deliberately <b>not</b> owned here, even though a round is. A round with
/// judges scoring every entry is this service's contended path, the same
/// role voting plays in celebrity-voting-service, and loading every entry or
/// score to touch one would read the whole table on exactly the path that
/// must not. See <c>IEntryRepository</c>/<c>IScoreRepository</c>.
/// </remarks>
public sealed class Competition : AggregateRoot, ITenantScopedEntity
{
    private readonly List<CompetitionRegistration> _registrations = [];
    private readonly List<CompetitionJudge> _judges = [];
    private readonly List<Round> _rounds = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }

    /// <summary>A master-data-service LookupItem id, Category = CompetitionType. Stored and never validated cross-service, the same stance every id like this already takes.</summary>
    public Guid? CompetitionTypeId { get; private set; }

    public DateTimeOffset RegistrationStartAt { get; private set; }
    public DateTimeOffset RegistrationEndAt { get; private set; }

    public CompetitionStatus Status { get; private set; }
    public Guid CreatedByMemberId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<CompetitionRegistration> Registrations => _registrations.AsReadOnly();
    public IReadOnlyCollection<CompetitionJudge> Judges => _judges.AsReadOnly();
    public IReadOnlyCollection<Round> Rounds => _rounds.AsReadOnly();

    private Competition() { }   // EF Core

    public static Competition Create(
        Guid tenantId,
        string title,
        string? description,
        Guid? competitionTypeId,
        DateTimeOffset registrationStartAt,
        DateTimeOffset registrationEndAt,
        Guid createdByMemberId,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Competition
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            CompetitionTypeId = competitionTypeId,
            RegistrationStartAt = registrationStartAt,
            RegistrationEndAt = registrationEndAt,
            Status = CompetitionStatus.Draft,
            CreatedByMemberId = createdByMemberId,
            CreatedAt = now,
        };
    }

    /// <summary>Whether registration is open right now: the status says so, and the clock agrees.</summary>
    public bool AcceptsRegistrations(DateTimeOffset now) =>
        Status == CompetitionStatus.RegistrationOpen
        && now >= RegistrationStartAt
        && now < RegistrationEndAt;

    /// <summary>
    /// Moves the competition on. Returns false when the move is not one this
    /// competition can make.
    /// </summary>
    /// <remarks>
    /// Strictly forward - Draft to RegistrationOpen to RegistrationClosed to
    /// InProgress to Closed - the same next-state-check shape
    /// <c>VotingCampaign.MoveTo</c> already uses. There is no route back.
    /// </remarks>
    public bool MoveTo(CompetitionStatus target, DateTimeOffset now)
    {
        var allowed = Status switch
        {
            CompetitionStatus.Draft => target == CompetitionStatus.RegistrationOpen,
            CompetitionStatus.RegistrationOpen => target == CompetitionStatus.RegistrationClosed,
            CompetitionStatus.RegistrationClosed => target == CompetitionStatus.InProgress,
            CompetitionStatus.InProgress => target == CompetitionStatus.Closed,
            _ => false,
        };

        if (!allowed)
        {
            return false;
        }

        var previous = Status;
        Status = target;

        Raise(new CompetitionStatusChangedDomainEvent(
            Id, TenantId, previous.ToString(), target.ToString(), now));

        return true;
    }

    public CompetitionRegistration? FindRegistration(Guid memberId) =>
        _registrations.FirstOrDefault(r => r.MemberId == memberId);

    public bool IsRegistered(Guid memberId) =>
        FindRegistration(memberId) is { Status: RegistrationStatus.Registered };

    /// <summary>
    /// Registers a member, or reactivates a withdrawn registration. Returns
    /// null only when registration is not open; an already-active
    /// registration is returned unchanged, which the handler reports as a
    /// no-op success rather than an error - the caller has done nothing
    /// wrong pressing the button twice.
    /// </summary>
    public CompetitionRegistration? Register(Guid memberId, DateTimeOffset now)
    {
        if (!AcceptsRegistrations(now))
        {
            return null;
        }

        var existing = FindRegistration(memberId);

        if (existing is { Status: RegistrationStatus.Registered })
        {
            return existing;
        }

        if (existing is not null)
        {
            existing.Reactivate(now);
            Raise(new CompetitionRegistrationCreatedDomainEvent(Id, TenantId, existing.Id, memberId, now));
            return existing;
        }

        var registration = new CompetitionRegistration(Id, memberId, now);
        _registrations.Add(registration);

        Raise(new CompetitionRegistrationCreatedDomainEvent(Id, TenantId, registration.Id, memberId, now));

        return registration;
    }

    /// <summary>The member's own withdrawal. Returns false when there is nothing to withdraw.</summary>
    public bool Withdraw(Guid memberId, DateTimeOffset now)
    {
        var registration = FindRegistration(memberId);

        if (registration is not { Status: RegistrationStatus.Registered })
        {
            return false;
        }

        registration.Withdraw(now);

        return true;
    }

    public CompetitionJudge? FindJudge(Guid memberId) =>
        _judges.FirstOrDefault(j => j.MemberId == memberId);

    public bool IsJudge(Guid memberId) => FindJudge(memberId) is not null;

    /// <summary>
    /// Assigns or removes a judge. Returns false when assigning somebody
    /// already a judge, or removing somebody who is not one.
    /// </summary>
    /// <remarks>
    /// A judge is simply a member the organiser named - no new platform
    /// role, the same "the president is just a member" reasoning
    /// volunteer-groups-service already established for its own group lead.
    /// </remarks>
    public bool AssignJudge(Guid memberId, bool assign, DateTimeOffset now)
    {
        var existing = FindJudge(memberId);

        if (assign)
        {
            if (existing is not null)
            {
                return false;
            }

            _judges.Add(new CompetitionJudge(Id, memberId, now));

            return true;
        }

        if (existing is null)
        {
            return false;
        }

        _judges.Remove(existing);

        return true;
    }

    public Round? FindRound(Guid roundId) => _rounds.FirstOrDefault(r => r.Id == roundId);

    /// <summary>
    /// Opens a new round. Returns null once the competition itself is
    /// Closed - a round created after the competition is over has nowhere
    /// to be scored.
    /// </summary>
    public Round? CreateRound(string name, DateTimeOffset now)
    {
        if (Status == CompetitionStatus.Closed)
        {
            return null;
        }

        var sequenceNumber = _rounds.Count == 0 ? 1 : _rounds.Max(r => r.SequenceNumber) + 1;

        var round = new Round(Id, name, sequenceNumber, now);

        _rounds.Add(round);

        return round;
    }

    /// <summary>Closes a round. Returns false when there is no such round, or it is already closed.</summary>
    public bool CloseRound(Guid roundId, DateTimeOffset now)
    {
        var round = FindRound(roundId);

        if (round is not { Status: RoundStatus.Open })
        {
            return false;
        }

        round.Close(now);

        Raise(new RoundClosedDomainEvent(Id, TenantId, roundId, now));

        return true;
    }

    /// <summary>Announces a round's ranking. Raised by the handler, which has computed it.</summary>
    public void AnnounceRoundResults(Guid roundId, IReadOnlyList<Guid> rankedEntryIds, DateTimeOffset now) =>
        Raise(new RoundResultsPublishedDomainEvent(Id, TenantId, roundId, rankedEntryIds, now));
}

public enum CompetitionStatus
{
    Draft = 1,
    RegistrationOpen = 2,
    RegistrationClosed = 3,
    InProgress = 4,
    Closed = 5,
}
