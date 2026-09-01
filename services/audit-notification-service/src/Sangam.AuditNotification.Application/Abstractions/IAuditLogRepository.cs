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
    /// broadcasts. Tenant-filtered by the DbContext, so this cannot cross Samaaj.
    /// </summary>
    /// <remarks>
    /// In-app only, and that filter is load-bearing rather than tidy. An emailed
    /// copy of a message is the same message; without the filter, every event
    /// that sends mail would also add a second identical entry to the member's
    /// notification list, which reads as the platform having told them twice.
    /// </remarks>
    Task<IReadOnlyList<Notification>> ListForRecipientAsync(
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
    Task<IReadOnlyList<Notification>> ListEveryChannelForRecipientAsync(
        Guid recipientUserId, int limit, CancellationToken cancellationToken = default);

    void Add(Notification notification);

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
