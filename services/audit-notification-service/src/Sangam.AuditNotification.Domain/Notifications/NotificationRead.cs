using Sangam.AuditNotification.Domain.Common;

namespace Sangam.AuditNotification.Domain.Notifications;

/// <summary>
/// One member having read one notification.
/// </summary>
/// <remarks>
/// <para>
/// A row of its own rather than a column on <see cref="Notification"/>, and the
/// reason is broadcasts. A Samaaj-wide announcement is a single notification row
/// with no recipient, shared by every member of that Samaaj - so the first
/// person to open it would have marked it read for everybody. Read-ness is a
/// fact about a person and a message, not about a message, and it needs a place
/// that can hold one per person.
/// </para>
/// <para>
/// Direct notifications could have kept a column and broadcasts gained a table,
/// but two mechanisms for one idea is how the second one gets forgotten - by the
/// erasure path, by the unread count, by whoever adds the next channel. So this
/// holds read state for both, and <c>Notification</c> has none: its
/// <c>Status</c> is now purely about delivery.
/// </para>
/// </remarks>
public sealed class NotificationRead : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid UserId { get; private set; }

    /// <summary>
    /// Denormalised from the notification so the tenant query filter applies
    /// here too, and so erasure and tenant checks do not need a join to decide
    /// what a row belongs to.
    /// </summary>
    public Guid TenantId { get; private set; }

    public DateTimeOffset ReadAt { get; private set; }

    private NotificationRead() { }

    public static NotificationRead Record(
        Guid notificationId, Guid userId, Guid tenantId, DateTimeOffset readAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            NotificationId = notificationId,
            UserId = userId,
            TenantId = tenantId,
            ReadAt = readAt,
        };
}
