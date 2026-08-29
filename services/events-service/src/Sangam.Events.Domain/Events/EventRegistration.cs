namespace Sangam.Events.Domain.Events;

/// <summary>
/// One member's place at an event, or their place in the queue for one.
/// </summary>
/// <remarks>
/// Owned by <see cref="SamaajEvent"/>: no independent factory, because a
/// registration cannot exist without the event having accepted it.
///
/// <see cref="RegisteredAt"/> is the waitlist order and is only moved when a
/// member leaves and comes back. Refreshing it on promotion would put a
/// promoted member behind people who joined the waitlist after them, which is
/// the one thing a waitlist must not do.
/// </remarks>
public sealed class EventRegistration
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid MemberId { get; private set; }
    public RegistrationStatus Status { get; private set; }

    /// <summary>When they joined the queue. The waitlist reads in this order.</summary>
    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    private EventRegistration() { }   // EF Core

    internal EventRegistration(
        Guid eventId, Guid memberId, RegistrationStatus status, DateTimeOffset registeredAt)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        MemberId = memberId;
        Status = status;
        RegisteredAt = registeredAt;
    }

    /// <summary>
    /// Brings a cancelled registration back, or promotes a waitlisted one.
    /// </summary>
    /// <remarks>
    /// <paramref name="now"/> resets the queue position only for somebody who
    /// had cancelled - they are rejoining the back of the queue, which is the
    /// point of having one. A promotion keeps the original time, because the
    /// wait is what earned the place.
    /// </remarks>
    internal void Reinstate(RegistrationStatus status, DateTimeOffset now)
    {
        if (Status == RegistrationStatus.Cancelled)
        {
            RegisteredAt = now;
        }

        Status = status;
        CancelledAt = null;
    }

    internal void Cancel(DateTimeOffset now)
    {
        Status = RegistrationStatus.Cancelled;
        CancelledAt = now;
    }
}
