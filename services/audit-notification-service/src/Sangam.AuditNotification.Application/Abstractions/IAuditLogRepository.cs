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
    /// <summary>Ignores the tenant query filter, for the same reason as above.</summary>
    Task<bool> AlreadyRaisedAsync(Guid sourceMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifications addressed to this member, plus the Samaaj-wide broadcasts.
    /// Tenant-filtered by the DbContext, so this cannot cross Samaaj.
    /// </summary>
    Task<IReadOnlyList<Notification>> ListForRecipientAsync(
        Guid recipientUserId, int limit, CancellationToken cancellationToken = default);

    void Add(Notification notification);
}
