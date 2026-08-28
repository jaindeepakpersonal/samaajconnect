using System.Text;
using Confluent.Kafka;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.IntegrationEvents;
using Sangam.MemberFamily.Application.IntegrationEvents.Commands.CompleteChildConversion;
using Sangam.MemberFamily.Application.IntegrationEvents.Commands.CreateProfileForNewUser;
using Sangam.MemberFamily.Application.IntegrationEvents.Commands.EraseMemberData;

namespace Sangam.MemberFamily.Infrastructure.Messaging;

/// <summary>
/// Consumes the platform events this service acts on.
/// </summary>
/// <remarks>
/// Offsets are committed manually, only after a message has been handled, which
/// makes consumption at-least-once to match the at-least-once publishing on the
/// other side. The handler deduplicates on the outbox message id, so a replay
/// after a crash is a no-op rather than a duplicate row.
/// </remarks>
public sealed class IntegrationEventConsumer(
    IServiceScopeFactory scopeFactory,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<ConsumerOptions> consumerOptions,
    ILogger<IntegrationEventConsumer> logger)
    : BackgroundService
{
    private readonly ConsumerOptions _options = consumerOptions.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() blocks, so it gets its own thread rather than tying up a
        // thread-pool thread for the lifetime of the process.
        Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = kafkaOptions.Value.BootstrapServers,
            GroupId = _options.GroupId,
            // Earliest for a brand-new group: an audit service joining late
            // should still capture the history still in retention.
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
        ConsumeResult<string, string> result,
        CancellationToken stoppingToken)
    {
        var envelope = ToEnvelope(result);

        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                // One consumer, three topics. A switch here rather than a
                // registry: at this size a lookup table would be more
                // indirection than the thing it indexes. The commands return
                // different payloads, so only the outcome is compared.
                Result outcome = envelope.Topic switch
                {
                    var t when t.Contains("child-conversion.completed", StringComparison.Ordinal) =>
                        await sender.Send(new CompleteChildConversionCommand(envelope), stoppingToken),
                    var t when t.Contains("user.erased", StringComparison.Ordinal) =>
                        await sender.Send(new EraseMemberDataCommand(envelope), stoppingToken),
                    _ => await sender.Send(new CreateProfileForNewUserCommand(envelope), stoppingToken),
                };

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

        // Committing a message we could not record loses it from the trail,
        // which is bad. Refusing to commit stalls the partition and loses every
        // event queued behind it, which is worse. The full payload goes out at
        // Critical so the row can be reconstructed from logs.
        logger.LogCritical(
            "Giving up on {MessageId} from {Topic} after {Attempts} attempts. Payload: {Payload}",
            envelope.MessageId, envelope.Topic, _options.MaxAttempts, envelope.Payload);

        return true;
    }

    internal static IntegrationEventEnvelope ToEnvelope(ConsumeResult<string, string> result)
    {
        var headers = result.Message.Headers;

        return new IntegrationEventEnvelope(
            // A message with no id header cannot be deduplicated by id, so it
            // gets a deterministic one derived from its coordinates - which are
            // themselves stable across a replay of the same record.
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
