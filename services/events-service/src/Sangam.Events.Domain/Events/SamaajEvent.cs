using Sangam.Events.Domain.Common;

namespace Sangam.Events.Domain.Events;

/// <summary>
/// Something a Samaaj is holding: a Paryushan lecture, a seva day, a sports
/// meet.
/// </summary>
/// <remarks>
/// Named <c>SamaajEvent</c> rather than <c>Event</c> because <c>event</c> is a
/// C# keyword and <c>Event</c> next to <c>IDomainEvent</c> in the same
/// namespace reads as the wrong thing entirely. The data model calls it Event;
/// this is the same aggregate.
///
/// <b>Capacity and the waitlist are the substance of it.</b> The member-portal
/// wireframe shows a "Full — Waitlist" pill and a "Join Waitlist" button on an
/// event that has filled up, and that is a real state rather than a label: a
/// registration past capacity is <see cref="RegistrationStatus.Waitlisted"/>,
/// and cancelling a confirmed place promotes the person who has waited longest.
/// Without that promotion a waitlist is a list nobody ever comes off, which is
/// worse than not offering one.
/// </remarks>
public sealed class SamaajEvent : AggregateRoot, ITenantScopedEntity
{
    private readonly List<EventRegistration> _registrations = [];

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset? EndAt { get; private set; }
    public string? Venue { get; private set; }

    /// <summary>Whether the Samaaj itself or one of its volunteer groups is holding this.</summary>
    public OrganizerType OrganizerType { get; private set; }

    /// <summary>The volunteer group's id, when a group is holding it.</summary>
    public Guid? OrganizerId { get; private set; }

    /// <summary>Who created it. The person answerable for it.</summary>
    public Guid CreatedByMemberId { get; private set; }

    public bool RegistrationEnabled { get; private set; }

    /// <summary>
    /// Null means unlimited. The wireframe's admin list shows "94" with no
    /// denominator for exactly this case, and a nullable column is what keeps
    /// "no limit" distinct from "a limit of zero".
    /// </summary>
    public int? Capacity { get; private set; }

    public EventStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<EventRegistration> Registrations => _registrations.AsReadOnly();

    private SamaajEvent() { }   // EF Core

    public static SamaajEvent Create(
        Guid tenantId,
        string title,
        string? description,
        DateTimeOffset startAt,
        DateTimeOffset? endAt,
        string? venue,
        OrganizerType organizerType,
        Guid? organizerId,
        Guid createdByMemberId,
        bool registrationEnabled,
        int? capacity,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var samaajEvent = new SamaajEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Description = Normalize(description),
            StartAt = startAt,
            EndAt = endAt,
            Venue = Normalize(venue),
            OrganizerType = organizerType,
            OrganizerId = organizerType == OrganizerType.VolunteerGroup ? organizerId : null,
            CreatedByMemberId = createdByMemberId,
            RegistrationEnabled = registrationEnabled,
            Capacity = capacity,

            // Created as a draft. The admin wireframe lists Draft alongside
            // Published, and writing an event down is not the same decision as
            // telling the whole Samaaj about it.
            Status = EventStatus.Draft,
            CreatedAt = now,
        };

        return samaajEvent;
    }

    /// <summary>How many places are taken. Waitlisted people do not count.</summary>
    public int RegisteredCount =>
        _registrations.Count(r => r.Status == RegistrationStatus.Registered);

    public int WaitlistedCount =>
        _registrations.Count(r => r.Status == RegistrationStatus.Waitlisted);

    /// <summary>True when there is a capacity and it is reached.</summary>
    public bool IsFull => Capacity is { } capacity && RegisteredCount >= capacity;

    public bool IsOpenForRegistration =>
        Status == EventStatus.Published && RegistrationEnabled;

    public EventRegistration? FindRegistration(Guid memberId) =>
        _registrations.FirstOrDefault(r => r.MemberId == memberId);

    /// <summary>
    /// Announces the event to the Samaaj. Returns false when it is already
    /// published, so a second click is not a second announcement.
    /// </summary>
    public bool Publish(DateTimeOffset now)
    {
        if (Status != EventStatus.Draft)
        {
            return false;
        }

        Status = EventStatus.Published;
        PublishedAt = now;

        Raise(new EventPublishedDomainEvent(
            Id, TenantId, OrganizerType.ToString(), OrganizerId, StartAt, Capacity, now));

        return true;
    }

    /// <summary>
    /// Calls the event off. Registrations are kept rather than deleted: people
    /// need to be told, and an attendee list that vanished is one nobody can
    /// notify.
    /// </summary>
    public bool Cancel(string? reason, DateTimeOffset now)
    {
        if (Status == EventStatus.Cancelled)
        {
            return false;
        }

        Status = EventStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = Normalize(reason);

        Raise(new EventCancelledDomainEvent(
            Id, TenantId, RegisteredCount + WaitlistedCount, now));

        return true;
    }

    /// <summary>
    /// Registers a member, or puts them on the waitlist when the event is full.
    /// Returns null when registration is not possible at all.
    /// </summary>
    /// <remarks>
    /// Re-registering after cancelling is allowed and re-uses the row, so a
    /// member who changes their mind twice does not accumulate history nobody
    /// asked for. Whether they come back to a place or to the waitlist depends
    /// on the room at that moment, not on the place they gave up - otherwise
    /// cancelling would be free and the waitlist would never move.
    /// </remarks>
    public EventRegistration? Register(Guid memberId, DateTimeOffset now)
    {
        if (!IsOpenForRegistration || StartAt <= now)
        {
            return null;
        }

        var existing = FindRegistration(memberId);

        if (existing is not null && existing.Status != RegistrationStatus.Cancelled)
        {
            // Already registered or already waiting. Nothing to do, and saying
            // so is the caller's job.
            return null;
        }

        var status = IsFull ? RegistrationStatus.Waitlisted : RegistrationStatus.Registered;

        if (existing is not null)
        {
            existing.Reinstate(status, now);
        }
        else
        {
            existing = new EventRegistration(Id, memberId, status, now);
            _registrations.Add(existing);
        }

        Raise(new EventRegistrationCreatedDomainEvent(
            Id, TenantId, memberId, status.ToString(), now));

        // Announced once, as the place is taken rather than every time somebody
        // looks. A Samaaj admin wants to know the moment an event fills up.
        if (status == RegistrationStatus.Registered && IsFull)
        {
            Raise(new EventCapacityReachedDomainEvent(Id, TenantId, Capacity!.Value, now));
        }

        return existing;
    }

    /// <summary>
    /// Gives up a place or leaves the waitlist. Returns the member promoted off
    /// the waitlist, if giving up this place freed one.
    /// </summary>
    public CancellationOutcome CancelRegistration(Guid memberId, DateTimeOffset now)
    {
        var registration = FindRegistration(memberId);

        if (registration is null || registration.Status == RegistrationStatus.Cancelled)
        {
            return new CancellationOutcome(false, null);
        }

        var freedAPlace = registration.Status == RegistrationStatus.Registered;

        registration.Cancel(now);

        if (!freedAPlace)
        {
            return new CancellationOutcome(true, null);
        }

        // The longest wait goes first. Any other order needs explaining to the
        // people it passes over.
        var next = _registrations
            .Where(r => r.Status == RegistrationStatus.Waitlisted)
            .OrderBy(r => r.RegisteredAt)
            .FirstOrDefault();

        if (next is null)
        {
            return new CancellationOutcome(true, null);
        }

        next.Reinstate(RegistrationStatus.Registered, now);

        Raise(new EventWaitlistPromotedDomainEvent(Id, TenantId, next.MemberId, now));

        return new CancellationOutcome(true, next.MemberId);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// <paramref name="PromotedMemberId"/> is the person who came off the waitlist
/// because this place was given up, if there was one.
/// </summary>
public sealed record CancellationOutcome(bool Cancelled, Guid? PromotedMemberId);

public enum OrganizerType
{
    Samaaj = 1,
    VolunteerGroup = 2,
}

public enum EventStatus
{
    Draft = 1,
    Published = 2,
    Cancelled = 3,
}

public enum RegistrationStatus
{
    Registered = 1,
    Waitlisted = 2,
    Cancelled = 3,
}
