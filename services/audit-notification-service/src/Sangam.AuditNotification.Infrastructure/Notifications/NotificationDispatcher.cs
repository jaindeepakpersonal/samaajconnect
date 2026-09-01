using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Notifications.Delivery;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Infrastructure.Notifications;

/// <summary>
/// Polls notifications waiting to leave the platform, hands each to the adapter
/// for its channel, and records what happened.
/// </summary>
/// <remarks>
/// <para>
/// Shaped like <c>OutboxDispatcher</c>, and separate from it on purpose. The
/// outbox moves events between services, where a duplicate costs nothing
/// because every consumer is idempotent. This moves messages to people, where a
/// duplicate is a second text message at midnight. That difference is why the
/// claim here is a locked, committed, single-statement UPDATE rather than the
/// outbox's plain read - see <c>NotificationRepository.ClaimPendingAsync</c>.
/// </para>
/// <para>
/// Delivery is still at-least-once, and cannot be otherwise: an adapter that is
/// interrupted between the provider accepting a message and this recording the
/// outcome will send it again. The claim narrows that window to a real crash
/// rather than a race between two healthy processes.
/// </para>
/// </remarks>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopeFactory,
    INotificationChannelRegistry channels,
    IOptions<NotificationDeliveryOptions> options,
    ILogger<NotificationDispatcher> logger)
    : BackgroundService
{
    private readonly NotificationDeliveryOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogWarning(
                "Notification dispatcher is disabled. Messages for {Channels} will queue and not be sent.",
                string.Join(", ", channels.Configured));

            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        logger.LogInformation(
            "Notification dispatcher started: polling every {Interval}s, batch {BatchSize}, channels {Channels}",
            interval.TotalSeconds,
            _options.BatchSize,
            string.Join(", ", channels.Configured));

        // Said out loud at startup, every start. A stand-in that quietly marks
        // messages Sent is the kind of thing a deployment inherits without
        // anyone deciding to, and the first sign would be a member saying they
        // never received something the platform reports as delivered.
        var pretending = channels.Configured
            .Where(channel => channels.For(channel) is LoggingNotificationChannel)
            .ToArray();

        if (pretending.Length > 0)
        {
            logger.LogWarning(
                "{Channels} are handled by the logging channel: messages are written to this log "
                + "and NOT delivered to anyone. Notifications will still be marked Sent. "
                + "Configure a real provider before relying on any of them.",
                string.Join(", ", pretending));

            if (_options.Logging.RevealContent)
            {
                logger.LogWarning(
                    "NotificationDelivery:Logging:RevealContent is on, so message bodies and full "
                    + "contact addresses are being written to this log. That is a copy of personal "
                    + "data outside the database. Local development only.");
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReleaseStalledAsync(stoppingToken);

                var handled = await DispatchBatchAsync(stoppingToken);

                // A full batch usually means more is waiting; go straight round
                // again rather than sleeping through a backlog.
                if (handled >= _options.BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Notification dispatch cycle failed; retrying in {Interval}s",
                    interval.TotalSeconds);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Notification dispatcher stopped");
    }

    /// <summary>
    /// Claims a batch, delivers it, and records every outcome. Returns how many
    /// notifications were handled, successfully or not.
    /// </summary>
    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        // Identifies this pass, so the read-back returns the rows this call
        // claimed and not one another dispatcher is holding.
        var claimId = Guid.NewGuid();

        var batch = await notifications.ClaimPendingAsync(
            claimId, _options.BatchSize, clock.UtcNow, cancellationToken);

        if (batch.Count == 0)
        {
            return 0;
        }

        foreach (var notification in batch)
        {
            await DeliverOneAsync(notification, clock, cancellationToken);
        }

        // CancellationToken.None deliberately. Every notification in this batch
        // has already been attempted, and losing the record of that on shutdown
        // would leave rows in Sending that the stall timeout has to rescue -
        // having sent them. The save is short and local; the thing worth
        // interrupting was the delivery, and that has finished.
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return batch.Count;
    }

    private async Task DeliverOneAsync(
        Notification notification,
        IDateTimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (channels.For(notification.Channel) is not { } channel)
        {
            // Permanent: nothing about waiting will make a provider appear, and
            // this is a deployment question rather than a delivery one.
            notification.RecordDeliveryFailure(
                $"No provider is configured for {notification.Channel} notifications.",
                permanent: true,
                clock.UtcNow);

            logger.LogError(
                "Notification {NotificationId} needs a {Channel} provider and none is registered",
                notification.Id,
                notification.Channel);

            return;
        }

        if (notification.Destination is not { Length: > 0 } destination)
        {
            // Create() refuses to leave an outbound notification without a
            // destination Pending, so reaching here means a row was edited
            // outside the aggregate. Recorded rather than thrown: the batch has
            // other messages in it that deserve to go out.
            notification.RecordDeliveryFailure(
                "No destination address on this notification.",
                permanent: true,
                clock.UtcNow);

            return;
        }

        try
        {
            var result = await channel.DeliverAsync(
                new OutboundMessage(
                    notification.Id,
                    notification.TenantId,
                    notification.Channel,
                    destination,
                    notification.Title,
                    notification.Body),
                cancellationToken);

            RecordOutcome(notification, result, clock.UtcNow);
        }
        catch (Exception exception)
        {
            // Transient, including on shutdown: an adapter interrupted
            // mid-request may or may not have sent the message, and the honest
            // reading of "do not know" on this platform is at-least-once.
            notification.RecordDeliveryFailure(
                $"{exception.GetType().Name} while delivering.",
                permanent: false,
                clock.UtcNow);

            logger.LogError(
                exception,
                "Delivering notification {NotificationId} over {Channel} threw on attempt {Attempt}",
                notification.Id,
                notification.Channel,
                notification.DeliveryAttempts);
        }
    }

    private void RecordOutcome(Notification notification, DeliveryResult result, DateTimeOffset now)
    {
        switch (result.Status)
        {
            case DeliveryStatus.Delivered:
                notification.MarkDelivered(now);
                return;

            case DeliveryStatus.PermanentFailure:
                notification.RecordDeliveryFailure(
                    result.Detail ?? "The provider rejected this message permanently.",
                    permanent: true,
                    now);

                logger.LogWarning(
                    "Notification {NotificationId} over {Channel} was rejected permanently: {Detail}",
                    notification.Id,
                    notification.Channel,
                    result.Detail);

                return;

            default:
                notification.RecordDeliveryFailure(
                    result.Detail ?? "The provider could not take this message.",
                    permanent: false,
                    now);

                if (notification.Status == NotificationStatus.Failed)
                {
                    logger.LogError(
                        "Notification {NotificationId} over {Channel} failed {Attempts} times "
                        + "and will not be retried: {Detail}",
                        notification.Id,
                        notification.Channel,
                        notification.DeliveryAttempts,
                        result.Detail);
                }

                return;
        }
    }

    /// <summary>
    /// Returns notifications abandoned mid-delivery to the queue.
    /// </summary>
    internal async Task<int> ReleaseStalledAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var stalledAfter = TimeSpan.FromMinutes(Math.Max(1, _options.StalledAfterMinutes));
        var now = clock.UtcNow;

        var stalled = await notifications.ListStalledAsync(
            now, stalledAfter, _options.BatchSize, cancellationToken);

        var released = stalled.Count(notification => notification.ReleaseStalledClaim(now, stalledAfter));

        if (released == 0)
        {
            return 0;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Returned {Count} notification(s) to the queue after they were left mid-delivery "
            + "for more than {Minutes} minute(s). They may already have been sent.",
            released,
            stalledAfter.TotalMinutes);

        return released;
    }
}
