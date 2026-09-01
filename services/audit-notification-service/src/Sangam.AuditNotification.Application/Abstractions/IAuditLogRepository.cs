using Sangam.AuditNotification.Application.Notifications.Queries.ListBroadcasts;
using Sangam.AuditNotification.Domain.AuditLogs;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Application.Abstractions;

public interface IAuditLogRepository
{
    /// <summary>
    /// True when this outbox row has already been recorded.
    /// </summary>
    /// <remarks>
    /// Ignores the tenant query filter deliberately. A Kafka consumer has no
    /// request and therefore no resolved tenant, so a filtered check would
    /// match nothing and every redelivered event would be recorded twice -
    /// which is exactly the failure the check exists to prevent.
    /// </remarks>
    Task<bool> AlreadyRecordedAsync(Guid sourceMessageId, CancellationToken cancellationToken = default);

    void Add(AuditLog auditLog);
}

/// <summary>
/// One notification as it looks to one member, which is the only way read state
/// is meaningful: a broadcast is a single row that a thousand people each read
/// separately, so "when was this read" has no answer until you say by whom.
/// </summary>
public sealed record MemberNotification(Notification Notification, DateTimeOffset? ReadAt);

public interface INotificationRepository
{
    /// <summary>
    /// True when this source event already produced a notification on this
    /// channel. Ignores the tenant query filter, for the same reason as above.
    /// </summary>
    /// <remarks>
    /// Per channel, not per event: one event legitimately raises an in-app
    /// notification and an emailed copy of it, and those are two rows. What
    /// must never happen is the same event producing the same row twice on a
    /// redelivery, which is what the unique index on
    /// (source_message_id, channel) guarantees underneath this check.
    /// </remarks>
    Task<bool> AlreadyRaisedAsync(
        Guid sourceMessageId, NotificationChannel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// In-app notifications addressed to this member, plus the Samaaj-wide
    /// broadcasts, each with when *this* member read it. Tenant-filtered by the
    /// DbContext, so this cannot cross Samaaj.
    /// </summary>
    /// <remarks>
    /// In-app only, and that filter is load-bearing rather than tidy. An emailed
    /// copy of a message is the same message; without the filter, every event
    /// that sends mail would also add a second identical entry to the member's
    /// notification list, which reads as the platform having told them twice.
    /// </remarks>
    Task<IReadOnlyList<MemberNotification>> ListForRecipientAsync(
        Guid recipientUserId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every notification for this member on every channel, for the DPDP s.11
    /// data export.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ListForRecipientAsync"/> because the two want
    /// opposite things. The member's notification list wants one entry per
    /// message; the export wants everything this service holds about them,
    /// which includes the fact that a message was also emailed and the address
    /// it went to. Sharing one method would have silently narrowed the export
    /// the day the list gained its in-app filter.
    /// </remarks>
    Task<IReadOnlyList<MemberNotification>> ListEveryChannelForRecipientAsync(
        Guid recipientUserId, int limit, CancellationToken cancellationToken = default);

    /// <summary>One notification, tenant-filtered. Null when it is not this Samaaj's.</summary>
    Task<Notification?> FindByIdAsync(Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>This Samaaj's broadcasts, newest first, with how many members have opened each.</summary>
    Task<IReadOnlyList<BroadcastResponse>> ListBroadcastsAsync(
        int limit, CancellationToken cancellationToken = default);

    void Add(Notification notification);

    /// <summary>
    /// Records that a member has read a notification, or does nothing if that
    /// was already true. Returns whether it wrote a row.
    /// </summary>
    /// <remarks>
    /// One statement rather than a read followed by a write, because opening the
    /// same notification twice at once is ordinary and the unique index would
    /// otherwise turn the loser into a 500.
    /// </remarks>
    Task<bool> TryRecordReadAsync(NotificationRead read, CancellationToken cancellationToken = default);

    Task<NotificationRead?> FindReadAsync(
        Guid notificationId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks every in-app notification this member can see as read, and returns
    /// how many were not already. Rows they have read stay as they were, so the
    /// timestamps are when each was actually first opened.
    /// </summary>
    Task<int> MarkEverythingReadAsync(
        Guid userId, Guid tenantId, DateTimeOffset readAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes ownership of up to <paramref name="batchSize"/> notifications
    /// waiting to be sent, marking them <see cref="NotificationStatus.Sending"/>
    /// and stamping them with <paramref name="claimId"/>, then returns them.
    /// </summary>
    /// <remarks>
    /// The claim is committed before delivery starts, so two dispatchers can
    /// never hold the same row. See the implementation for why this is a single
    /// SQL statement rather than a method on the aggregate.
    /// </remarks>
    Task<IReadOnlyList<Notification>> ClaimPendingAsync(
        Guid claimId, int batchSize, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifications left in <see cref="NotificationStatus.Sending"/> for longer
    /// than <paramref name="stalledAfter"/>, so a caller can return them to the
    /// queue. Ignores the tenant query filter: the dispatcher has no request.
    /// </summary>
    Task<IReadOnlyList<Notification>> ListStalledAsync(
        DateTimeOffset now, TimeSpan stalledAfter, int limit, CancellationToken cancellationToken = default);
}
