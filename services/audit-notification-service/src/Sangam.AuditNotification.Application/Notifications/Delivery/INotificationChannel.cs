using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Application.Notifications.Delivery;

/// <summary>
/// One message, ready to leave the platform.
/// </summary>
/// <remarks>
/// Flattened out of the <see cref="Notification"/> aggregate on purpose. A
/// channel is an adapter around somebody else's API and has no business being
/// able to change delivery state - it is handed what it needs to send and
/// answers with what happened. The dispatcher owns the transition.
/// </remarks>
public sealed record OutboundMessage(
    Guid NotificationId,
    Guid TenantId,
    NotificationChannel Channel,
    string Destination,
    string Title,
    string Body);

public enum DeliveryStatus
{
    Delivered = 1,

    /// <summary>
    /// Worth trying again - a provider timeout, a refused connection, a 503.
    /// </summary>
    TransientFailure = 2,

    /// <summary>
    /// Will fail the same way every time - a malformed address, a recipient who
    /// has unsubscribed, credentials the provider rejects. Retrying is not
    /// harmless: it burns quota and, if the address belongs to somebody else,
    /// it sends them the message again.
    /// </summary>
    PermanentFailure = 3,
}

/// <param name="Detail">
/// Why, for the operator reading <c>failure_reason</c> later. It is written to
/// the database, so it must describe the failure without repeating the message
/// or the address - both are personal data and the reason column is not where
/// a copy of them belongs.
/// </param>
public sealed record DeliveryResult(DeliveryStatus Status, string? Detail = null)
{
    public static DeliveryResult Delivered() => new(DeliveryStatus.Delivered);

    public static DeliveryResult Transient(string detail) => new(DeliveryStatus.TransientFailure, detail);

    public static DeliveryResult Permanent(string detail) => new(DeliveryStatus.PermanentFailure, detail);
}

/// <summary>
/// Carries a message to one recipient over one transport.
/// </summary>
/// <remarks>
/// The seam a real provider plugs into. An implementation is expected to be a
/// thin adapter: build the provider request, send it, translate the answer into
/// a <see cref="DeliveryResult"/>. It decides nothing about who gets messaged,
/// how often, or whether to give up.
///
/// Implementations must not throw for a failed send. A thrown exception is
/// treated as transient by the dispatcher, which is the safe default but a
/// worse answer than the adapter itself saying which kind of failure it was.
/// </remarks>
public interface INotificationChannel
{
    NotificationChannel Channel { get; }

    Task<DeliveryResult> DeliverAsync(OutboundMessage message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Finds the adapter for a channel. Null when nothing is configured to carry
/// that kind of message, which is a permanent failure rather than an error: the
/// platform has channels it has not been given a provider for.
/// </summary>
public interface INotificationChannelRegistry
{
    INotificationChannel? For(NotificationChannel channel);

    IReadOnlyCollection<NotificationChannel> Configured { get; }
}
