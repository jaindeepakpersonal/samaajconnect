namespace Sangam.Events.Application.Events;

/// <summary>
/// An event as the list and detail screens show it.
/// </summary>
/// <remarks>
/// <paramref name="OrganizerId"/> is an id, not a name. Group names live in
/// volunteer-groups-service and member names in member-family-service;
/// resolving either here would mean a call per row for a list.
/// </remarks>
public sealed record EventResponse(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset StartAt,
    DateTimeOffset? EndAt,
    string? Venue,

    /// <summary>A master-data-service LookupItem id, or null. Never a name -
    /// resolving one is the portal's job, the same as OrganizerId below.</summary>
    Guid? EventTypeId,

    /// <summary>The locality the event is for, or null for the whole Samaaj.</summary>
    Guid? LocalityId,
    string OrganizerType,
    Guid? OrganizerId,
    string Status,
    bool RegistrationEnabled,

    /// <summary>Null means no limit, which is a different thing from a limit of zero.</summary>
    int? Capacity,
    int RegisteredCount,
    int WaitlistedCount,

    /// <summary>True when there is a capacity and it is reached.</summary>
    bool IsFull,

    /// <summary>
    /// What the asking member's own registration says: Registered, Waitlisted,
    /// Cancelled, or null if they have never registered. This is what the
    /// wireframe's "Your Status" card and its RSVP button read.
    /// </summary>
    string? MyRegistrationStatus,
    DateTimeOffset? CancelledAt,
    string? CancellationReason,
    DateTimeOffset CreatedAt,

    /// <summary>The programmes, by date then start time. Empty when the event has no schedule.</summary>
    IReadOnlyList<EventScheduleItemResponse>? Schedule = null,

    /// <summary>
    /// Published, started and not yet over (an event with no end is taken to
    /// last a day). Only the list and the detail read the clock to say so;
    /// anywhere else it is false.
    /// </summary>
    bool IsHappeningNow = false,

    /// <summary>
    /// Where the event's cover photo is fetched from, with the token, or null
    /// when it has none.
    /// </summary>
    string? PhotoUrl = null);

/// <summary>One programme: a date, its name, and wall-clock times - EndTime is optional.</summary>
public sealed record EventScheduleItemResponse(
    Guid Id,
    DateOnly Date,
    string ProgramName,
    TimeOnly StartTime,
    TimeOnly? EndTime);

/// <summary>
/// One attendee, for the organiser's list.
/// </summary>
/// <remarks>
/// An id and a status, no name. Turning ids into names is the portal's job -
/// and an attendee list is a list of who is going somewhere, which is not a
/// thing this service should be handing out more of than it has to.
/// </remarks>
public sealed record AttendeeResponse(
    Guid MemberId,
    string Status,
    DateTimeOffset RegisteredAt);

/// <summary>
/// <paramref name="Status"/> is Registered or Waitlisted - which one the member
/// got is the whole answer they are waiting for.
/// </summary>
public sealed record RegistrationResponse(Guid EventId, string Status, int Position);

/// <summary>
/// <paramref name="PromotedMemberId"/> is whoever came off the waitlist because
/// this place was given up, if anyone did.
/// </summary>
public sealed record CancelRegistrationResponse(
    Guid EventId,
    bool Cancelled,
    Guid? PromotedMemberId);
