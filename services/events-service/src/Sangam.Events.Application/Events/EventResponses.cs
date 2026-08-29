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
    DateTimeOffset CreatedAt);

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
