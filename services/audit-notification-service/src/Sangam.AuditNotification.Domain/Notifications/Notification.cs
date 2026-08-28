using Sangam.AuditNotification.Domain.Common;

namespace Sangam.AuditNotification.Domain.Notifications;

/// <summary>
/// A message for one member, or for a whole Samaaj when
/// <see cref="RecipientUserId"/> is null.
/// </summary>
public sealed class Notification : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Null means a broadcast to the whole Samaaj.</summary>
    public Guid? RecipientUserId { get; private set; }

    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; }

    /// <summary>
    /// The outbox row that caused this notification, so a redelivered event
    /// does not produce a second copy of the same message.
    /// </summary>
    public Guid SourceMessageId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    private Notification() { }

    public static Notification Create(
        Guid tenantId,
        Guid? recipientUserId,
        string title,
        string body,
        NotificationChannel channel,
        Guid sourceMessageId,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecipientUserId = recipientUserId,
            Title = title.Trim(),
            Body = body.Trim(),
            Channel = channel,
            // In-app notifications are readable the moment the row exists, so
            // they start Sent. Email/SMS will start Pending once a real
            // delivery channel exists to move them along.
            Status = channel == NotificationChannel.InApp
                ? NotificationStatus.Sent
                : NotificationStatus.Pending,
            SourceMessageId = sourceMessageId,
            CreatedAt = createdAt,
        };
    }

    public void MarkRead(DateTimeOffset readAt)
    {
        if (Status == NotificationStatus.Read)
        {
            return;
        }

        Status = NotificationStatus.Read;
        ReadAt = readAt;
    }
}
