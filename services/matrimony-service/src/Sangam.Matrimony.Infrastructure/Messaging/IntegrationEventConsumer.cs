using System.Text;
using Confluent.Kafka;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.IntegrationEvents;
using Sangam.Matrimony.Application.IntegrationEvents.Commands.EraseMatrimonyData;
using Sangam.Matrimony.Application.IntegrationEvents.Commands.RecordMaritalStatus;

namespace Sangam.Matrimony.Infrastructure.Messaging;

/// <summary>
/// Consumes the one platform event this service acts on. Mirrors
/// member-family-service's own `IntegrationEventConsumer` exactly - offsets
/// committed manually, only after a message has been handled, so
/// consumption is at-least-once to match at-least-once publishing; the
/// handler is idempotent against a replay by construction (erasing an
/// already-erased profile, or de-identifying an already-de-identified
/// proposal, is a no-op).
/// </summary>
public sealed class IntegrationEventConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<ConsumerOptions> consumerOptions,
    ILogger<IntegrationEventConsumer> logger)
    : BackgroundService
{
    private readonly ConsumerOptions _options = consumerOptions.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = false,
            TopicMetadataRefreshIntervalMs = _options.MetadataRefreshIntervalMilliseconds,
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
                logger.LogError("Kafka consumer error: {Reason} (code {Code})", error.Reason, error.Code))
            .Build();

        consumer.Subscribe(_options.Topics);

        logger.LogInformation(
            "Consumer subscribed to {Topics} as group {GroupId}",
            string.Join(", ", _options.Topics),
            _options.GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;

                try
                {
                    result = consumer.Consume(TimeSpan.FromSeconds(1));
                }
                catch (ConsumeException exception)
                {
                    logger.LogError(exception, "Failed to consume from Kafka");
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                if (await HandleWithRetriesAsync(result, stoppingToken))
                {
                    consumer.StoreOffset(result);
                    consumer.Commit(result);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            consumer.Close();
            logger.LogInformation("Consumer stopped");
        }
    }

    private async Task<bool> HandleWithRetriesAsync(
        ConsumeResult<string, string> result, CancellationToken stoppingToken)
    {
        var envelope = ToEnvelope(result);

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                // Each topic has its own command; ConsumerOptions subscribes to no other.
                Result outcome = envelope.Topic == "members.marital-status.changed.v1"
                    ? await sender.Send(new RecordMaritalStatusCommand(envelope), stoppingToken)
                    : await sender.Send(new EraseMatrimonyDataCommand(envelope), stoppingToken);

                if (outcome.IsSuccess)
                {
                    return true;
                }

                logger.LogWarning(
                    "Recording {MessageId} from {Topic} failed with {ErrorCode} (attempt {Attempt})",
                    envelope.MessageId, envelope.Topic, outcome.Error.Code, attempt);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Recording {MessageId} from {Topic} threw (attempt {Attempt})",
                    envelope.MessageId, envelope.Topic, attempt);
            }

            if (attempt < _options.MaxAttempts)
            {
                await Task.Delay(_options.RetryDelayMilliseconds * attempt, stoppingToken);
            }
        }

        logger.LogCritical(
            "Giving up on {MessageId} from {Topic} after {Attempts} attempts. Payload: {Payload}",
            envelope.MessageId, envelope.Topic, _options.MaxAttempts, envelope.Payload);

        return true;
    }

    internal static IntegrationEventEnvelope ToEnvelope(ConsumeResult<string, string> result)
    {
        var headers = result.Message.Headers;

        return new IntegrationEventEnvelope(
            ReadGuid(headers, EventHeaders.MessageId) ?? DeterministicId(result),
            ReadGuid(headers, EventHeaders.TenantId) ?? ParseGuid(result.Message.Key) ?? Guid.Empty,
            result.Topic,
            ReadString(headers, EventHeaders.EventType) ?? result.Topic,
            result.Message.Value ?? "{}",
            ReadDate(headers, EventHeaders.OccurredAt) ?? result.Message.Timestamp.UtcDateTime);
    }

    private static Guid DeterministicId(ConsumeResult<string, string> result)
    {
        var coordinates = $"{result.Topic}:{result.Partition.Value}:{result.Offset.Value}";

        return new Guid(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(coordinates)));
    }

    private static string? ReadString(Headers? headers, string key) =>
        headers is not null && headers.TryGetLastBytes(key, out var bytes)
            ? Encoding.UTF8.GetString(bytes)
            : null;

    private static Guid? ReadGuid(Headers? headers, string key) => ParseGuid(ReadString(headers, key));

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var parsed) ? parsed : null;

    private static DateTimeOffset? ReadDate(Headers? headers, string key) =>
        DateTimeOffset.TryParse(ReadString(headers, key), out var parsed) ? parsed : null;
}
