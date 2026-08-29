using Sangam.Events.Domain.Common;

namespace Sangam.Events.Domain.Events;

/// <summary>
/// An event was announced to the Samaaj.
/// </summary>
/// <remarks>
/// Carries the start time and capacity but not the title, description or
/// venue. audit-notification-service records payloads verbatim into an
/// append-only table, and the free text is the Samaaj's own copy - the ids and
/// the shape are what another service would act on.
/// </remarks>
public sealed record EventPublishedDomainEvent(
    Guid EventId,
    Guid TenantId,
    string OrganizerType,
    Guid? OrganizerId,
    DateTimeOffset StartAt,
    int? Capacity,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "events.event.published.v1";
}

/// <summary>
/// Somebody registered, or joined the waitlist. <paramref name="Status"/> says
/// which, so a notification service can tell them the right thing.
/// </summary>
public sealed record EventRegistrationCreatedDomainEvent(
    Guid EventId,
    Guid TenantId,
    Guid MemberId,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "events.registration.created.v1";
}

/// <summary>
/// The event just filled up. Raised once, as the last place is taken, rather
/// than every time somebody looks at a full event.
/// </summary>
public sealed record EventCapacityReachedDomainEvent(
    Guid EventId,
    Guid TenantId,
    int Capacity,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "events.capacity.reached.v1";
}

/// <summary>
/// Somebody came off the waitlist because a place was given up. This is the
/// event a notification channel will care about most: a member who is told
/// nothing has effectively not been promoted.
/// </summary>
public sealed record EventWaitlistPromotedDomainEvent(
    Guid EventId,
    Guid TenantId,
    Guid MemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "events.waitlist.promoted.v1";
}

/// <summary>
/// The event was called off. Carries how many people were expecting it, which
/// is the number that decides how urgently they need telling.
/// </summary>
public sealed record EventCancelledDomainEvent(
    Guid EventId,
    Guid TenantId,
    int AffectedRegistrations,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "events.event.cancelled.v1";
}
