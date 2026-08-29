using Sangam.Events.Domain.Events;

namespace Sangam.Events.Application.Events;

/// <summary>
/// The one place an event becomes a response, so there is one place to check
/// that nothing leaks out of it.
/// </summary>
internal static class EventMappings
{
    public static EventResponse ToResponse(this SamaajEvent samaajEvent, Guid viewerId) => new(
        samaajEvent.Id,
        samaajEvent.Title,
        samaajEvent.Description,
        samaajEvent.StartAt,
        samaajEvent.EndAt,
        samaajEvent.Venue,
        samaajEvent.OrganizerType.ToString(),
        samaajEvent.OrganizerId,
        samaajEvent.Status.ToString(),
        samaajEvent.RegistrationEnabled,
        samaajEvent.Capacity,
        samaajEvent.RegisteredCount,
        samaajEvent.WaitlistedCount,
        samaajEvent.IsFull,

        // A cancelled registration reads as null rather than "Cancelled": the
        // screen asks "am I going?", and someone who cancelled is in the same
        // position as someone who never registered.
        samaajEvent.FindRegistration(viewerId) is { Status: not RegistrationStatus.Cancelled } mine
            ? mine.Status.ToString()
            : null,
        samaajEvent.CancelledAt,
        samaajEvent.CancellationReason,
        samaajEvent.CreatedAt);

    public static IReadOnlyList<AttendeeResponse> ToAttendees(this SamaajEvent samaajEvent) =>
    [
        .. samaajEvent.Registrations
            .Where(r => r.Status != RegistrationStatus.Cancelled)

            // Confirmed places first, then the waitlist in the order it formed
            // - which is the order it will be drawn from.
            .OrderBy(r => r.Status == RegistrationStatus.Waitlisted)
            .ThenBy(r => r.RegisteredAt)
            .Select(r => new AttendeeResponse(r.MemberId, r.Status.ToString(), r.RegisteredAt))
    ];
}
