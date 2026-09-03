using System.Reflection;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sangam.Timeline.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Empty on purpose. Every service names itself in its own appsettings.json,
    /// and a default naming any one service is a default that names the *wrong*
    /// service in the other nine - which is how eight producers came to identify
    /// themselves to the broker as member-family-service. Empty falls back to the
    /// running assembly, so an unconfigured service is honestly named rather than
    /// impersonating one that is configured.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
}

/// <summary>
/// Native Confluent producer - no MassTransit anywhere in this repo
/// (CLAUDE.md section 5, ARCHITECTURE.md section 4). Registered as a singleton:
/// the underlying producer is thread-safe and expensive to build.
/// </summary>
public sealed class KafkaProducer : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IOptions<KafkaOptions> options, ILogger<KafkaProducer> logger)
    {
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = options.Value.BootstrapServers,
            ClientId = string.IsNullOrWhiteSpace(options.Value.ClientId)
                ? DefaultClientId()
                : options.Value.ClientId,
            // The Outbox already guarantees at-least-once delivery, so the
            // producer only has to avoid silently losing an acknowledged write.
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 3,
        };

        _producer = new ProducerBuilder<string, string>(config)
            .SetErrorHandler((_, error) =>
                _logger.LogError("Kafka producer error: {Reason} (code {Code})", error.Reason, error.Code))
            .Build();
    }

    /// <summary>
    /// The running assembly, when nothing configured a name. Wrong-but-plausible
    /// beats wrong-and-confident: a broker log naming Sangam.Boli.Api points at the
    /// service that published, where a copied constant pointed at a service that
    /// did not.
    /// </summary>
    private static string DefaultClientId() =>
        Assembly.GetEntryAssembly()?.GetName().Name ?? "sangam-service";

    public async Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var message = new Message<string, string>
        {
            Key = envelope.Key,
            Value = envelope.Payload,
            Headers =
            [
                new Header(EventHeaders.MessageId, Encoding.UTF8.GetBytes(envelope.MessageId.ToString())),
                new Header(EventHeaders.EventType, Encoding.UTF8.GetBytes(envelope.EventType)),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(envelope.Key)),
                new Header(EventHeaders.OccurredAt, Encoding.UTF8.GetBytes(envelope.OccurredAt.ToString("O"))),
            ],
        };

        var delivery = await _producer.ProduceAsync(envelope.Topic, message, cancellationToken);

        _logger.LogDebug(
            "Published to {Topic} partition {Partition} offset {Offset}",
            delivery.Topic,
            delivery.Partition.Value,
            delivery.Offset.Value);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
