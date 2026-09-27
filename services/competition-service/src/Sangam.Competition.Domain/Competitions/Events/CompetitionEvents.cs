using Sangam.Competition.Domain.Common;

namespace Sangam.Competition.Domain.Competitions.Events;

/// <summary>The competition moved on. Carries the previous status per SECURITY-CHECKLIST.md.</summary>
public sealed record CompetitionStatusChangedDomainEvent(
    Guid CompetitionId,
    Guid TenantId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "competition.competition.status-changed.v1";
}

/// <summary>A member registered (or re-registered after withdrawing).</summary>
public sealed record CompetitionRegistrationCreatedDomainEvent(
    Guid CompetitionId,
    Guid TenantId,
    Guid RegistrationId,
    Guid MemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "competition.registration.created.v1";
}

/// <summary>
/// A round closed. Separate from a general status change because it is the
/// moment scoring stops and publishing becomes possible - the fact a
/// notification channel would care about most.
/// </summary>
public sealed record RoundClosedDomainEvent(
    Guid CompetitionId,
    Guid TenantId,
    Guid RoundId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "competition.round.closed.v1";
}

/// <summary>
/// A round's ranking, in order. Entry ids rather than member ids or names -
/// audit-notification-service records payloads verbatim into an append-only
/// table, and an id that means nothing without this service's own tables is
/// the right amount to put there, the same restraint celebrity-voting-service's
/// own <c>ResultsPublishedDomainEvent</c> already takes.
/// </summary>
public sealed record RoundResultsPublishedDomainEvent(
    Guid CompetitionId,
    Guid TenantId,
    Guid RoundId,
    IReadOnlyList<Guid> RankedEntryIds,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "competition.round.results-published.v1";
}

/// <summary>A competition's details were corrected. Ids and who; the details are on the competition.</summary>
public sealed record CompetitionUpdatedDomainEvent(
    Guid CompetitionId,
    Guid TenantId,
    Guid UpdatedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "competition.competition.updated.v1";
}
