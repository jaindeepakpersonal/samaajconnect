namespace Sangam.Timeline.Application.IntegrationEvents;

/// <summary>
/// One event as it arrived from Kafka, headers and all. Mirrors the envelope
/// the publishing service's OutboxDispatcher sends.
/// </summary>
public sealed record IntegrationEventEnvelope(
    Guid MessageId,
    Guid TenantId,
    string Topic,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt);
