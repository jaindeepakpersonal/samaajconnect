using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sangam.AuditNotification.Application.Notifications.Delivery;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Infrastructure.Notifications;

/// <summary>
/// Stands in for a real provider by writing the message to the log.
/// </summary>
/// <remarks>
/// <para>
/// <b>This adapter delivers nothing.</b> It exists so the platform can have a
/// notification channel before it has an email or SMS provider: everything
/// upstream of the transport - deciding a message is due, addressing it,
/// queueing it, retrying it, giving up on it - is real, and the last step is a
/// log line. Swapping in a provider is one class and one registration.
/// </para>
/// <para>
/// It reports <see cref="DeliveryStatus.Delivered"/>, which marks the row Sent.
/// That is the only useful answer - a channel that always failed would make
/// every feature built on it untestable - but it means a Sent notification
/// under this adapter means "handed to the channel", not "reached a person". No
/// obligation that depends on someone actually being told is discharged by this
/// adapter, and the DPDP breach-notification duty is the case that matters:
/// see docs/product/DPDP-COMPLIANCE.md.
/// </para>
/// <para>
/// By default it logs who and what, not the address or the message body - see
/// <see cref="LoggingChannelOptions.RevealContent"/>.
/// </para>
/// </remarks>
public sealed class LoggingNotificationChannel : INotificationChannel
{
    private readonly NotificationDeliveryOptions _options;
    private readonly ILogger<LoggingNotificationChannel> _logger;

    public LoggingNotificationChannel(
        NotificationChannel channel,
        IOptions<NotificationDeliveryOptions> options,
        ILogger<LoggingNotificationChannel> logger)
    {
        Channel = channel;
        _options = options.Value;
        _logger = logger;
    }

    public NotificationChannel Channel { get; }

    public Task<DeliveryResult> DeliverAsync(
        OutboundMessage message,
        CancellationToken cancellationToken = default)
    {
        if (_options.Logging.RevealContent)
        {
            _logger.LogInformation(
                "NOT SENT (logging channel) {Channel} notification {NotificationId} for Samaaj "
                + "{TenantId} to {Destination}. {Title}: {Body}",
                Channel,
                message.NotificationId,
                message.TenantId,
                message.Destination,
                message.Title,
                message.Body);
        }
        else
        {
            _logger.LogInformation(
                "NOT SENT (logging channel) {Channel} notification {NotificationId} for Samaaj "
                + "{TenantId} to {Destination}. {Title}",
                Channel,
                message.NotificationId,
                message.TenantId,
                ContactRedaction.Redact(message.Destination),
                message.Title);
        }

        return Task.FromResult(DeliveryResult.Delivered());
    }
}
