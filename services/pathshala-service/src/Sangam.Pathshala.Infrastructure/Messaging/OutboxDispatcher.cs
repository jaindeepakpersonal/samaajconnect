using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sangam.Pathshala.Infrastructure.Persistence;

namespace Sangam.Pathshala.Infrastructure.Messaging;

/// <summary>
/// Polls unsent Outbox rows, publishes them to Kafka, marks them sent
/// (CLAUDE.md section 5). Deliberately at-least-once: a crash between the
/// Kafka ack and the mark-sent update republishes the row, so consumers must
/// be idempotent. Seeing an event twice is cheaper than losing one.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));

        logger.LogInformation(
            "Outbox dispatcher started: polling every {Interval}s, batch {BatchSize}",
            interval.TotalSeconds,
            _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await DispatchBatchAsync(stoppingToken);

                // A full batch usually means more is waiting; go straight round
                // again rather than sleeping through a backlog.
                if (dispatched >= _options.BatchSize)
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
                    "Outbox dispatch cycle failed; retrying in {Interval}s",
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

        logger.LogInformation("Outbox dispatcher stopped");
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PathshalaDbContext>();

        var messages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.Attempts < _options.MaxAttempts)
            .OrderBy(m => m.OccurredAt)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        var dispatched = 0;

        foreach (var message in messages)
        {
            try
            {
                await publisher.PublishAsync(
                    new OutboxEnvelope(
                        message.Id,
                        message.Topic,
                        message.TenantId.ToString(),
                        message.Type,
                        message.Payload,
                        message.OccurredAt),
                    cancellationToken);

                message.ProcessedAt = DateTimeOffset.UtcNow;
                message.Error = null;
                dispatched++;
            }
            catch (Exception exception)
            {
                message.Attempts++;
                message.Error = Truncate(exception.Message, 2000);

                logger.LogError(
                    exception,
                    "Failed to publish outbox message {MessageId} to {Topic} (attempt {Attempts})",
                    message.Id,
                    message.Topic,
                    message.Attempts);

                if (message.Attempts >= _options.MaxAttempts)
                {
                    logger.LogCritical(
                        "Outbox message {MessageId} exhausted {MaxAttempts} attempts and needs manual intervention",
                        message.Id,
                        _options.MaxAttempts);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return dispatched;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
