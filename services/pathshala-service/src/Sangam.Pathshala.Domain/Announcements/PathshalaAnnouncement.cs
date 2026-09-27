using Sangam.Pathshala.Domain.Common;

namespace Sangam.Pathshala.Domain.Announcements;

/// <summary>
/// A message from a Pathshala to the parents of every child enrolled at it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored here, and announced by id.</b> The text lives in this service, and
/// parents read it on the member portal's Pathshala screen. What leaves the
/// service is ids only: <see cref="AnnouncementSentDomainEvent"/> (who sent it,
/// to how many) and one <see cref="AnnouncementAddressedDomainEvent"/> per
/// parent, which audit-notification-service turns into that parent's in-app
/// notification saying there is something to read. The message itself
/// never reaches the append-only audit log, where a typo or a phone number in it
/// could not be taken back.
/// </para>
/// <para>
/// <b>Sent by a teacher of this Pathshala or somebody who runs it</b> - an
/// Area Admin for their own locality's, a Samaaj Admin for any (the user's
/// requirement). The recipients are fixed when it is sent: the parents of every
/// child with a place or a request at that moment.
/// </para>
/// </remarks>
public sealed class PathshalaAnnouncement : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PathshalaId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public Guid SentBy { get; private set; }
    public DateTimeOffset SentAt { get; private set; }

    /// <summary>How many parents it went to. A count, not the list - that is on the event.</summary>
    public int RecipientCount { get; private set; }

    private PathshalaAnnouncement() { }   // EF Core

    public static PathshalaAnnouncement Send(
        Guid tenantId,
        Guid pathshalaId,
        string title,
        string body,
        IReadOnlyCollection<Guid> recipientMemberIds,
        Guid sentBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var recipients = recipientMemberIds.Distinct().ToList();

        var announcement = new PathshalaAnnouncement
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PathshalaId = pathshalaId,
            Title = title.Trim(),
            Body = body.Trim(),
            SentBy = sentBy,
            SentAt = now,
            RecipientCount = recipients.Count,
        };

        announcement.Raise(new AnnouncementSentDomainEvent(
            announcement.Id, tenantId, pathshalaId, sentBy, recipients.Count, now));

        // One per parent, because audit-notification-service raises one in-app
        // notification per event - its (source message, channel) unique index
        // is what keeps a redelivery from notifying anybody twice, and it would
        // refuse a second notification from the same message.
        foreach (var recipient in recipients)
        {
            announcement.Raise(new AnnouncementAddressedDomainEvent(
                announcement.Id, tenantId, pathshalaId, recipient, now));
        }

        return announcement;
    }
}

/// <summary>
/// An announcement went out: who sent it, and to how many. Ids only - the title
/// and body stay in this service.
/// </summary>
public sealed record AnnouncementSentDomainEvent(
    Guid AnnouncementId,
    Guid TenantId,
    Guid PathshalaId,
    Guid SentBy,
    int RecipientCount,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.announcement.sent.v1";
}

/// <summary>
/// One parent has an announcement to read. audit-notification-service turns
/// this into their in-app notification. Ids only.
/// </summary>
public sealed record AnnouncementAddressedDomainEvent(
    Guid AnnouncementId,
    Guid TenantId,
    Guid PathshalaId,
    Guid RecipientMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.announcement.addressed.v1";
}
