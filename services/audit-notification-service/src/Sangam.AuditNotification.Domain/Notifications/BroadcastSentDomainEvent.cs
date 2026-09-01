using Sangam.AuditNotification.Domain.Common;

namespace Sangam.AuditNotification.Domain.Notifications;

/// <summary>
/// A Samaaj was told something by one of its administrators.
/// </summary>
/// <remarks>
/// The first event this service publishes, and it publishes it to itself: the
/// consumer subscribes to every versioned topic, so this comes back around and
/// becomes an audit row. That matters because a broadcast is an administrative
/// act - one person putting a message in front of everybody - and until now the
/// only trace of it would have been a log line and a row with no author.
///
/// The title travels; the body does not. Audit payloads are kept verbatim and
/// forever, and the body is up to 2000 characters that are already stored, once,
/// on the notification this event names. The title is enough to recognise which
/// announcement a row refers to.
/// </remarks>
public sealed record BroadcastSentDomainEvent(
    Guid NotificationId,
    Guid TenantId,
    string Title,
    Guid SentBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "notifications.broadcast.sent.v1";
}
