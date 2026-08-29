namespace Sangam.CelebrityVoting.Infrastructure.Messaging;

/// <summary>
/// One outbox row on its way to the broker.
/// </summary>
/// <param name="MessageId">
/// The outbox row id, carried through as a Kafka header. Delivery is
/// at-least-once, so this is what lets a consumer recognise a replay and skip
/// it. Without it "consumers must be idempotent" is advice nobody can act on.
/// </param>
public sealed record OutboxEnvelope(
    Guid MessageId,
    string Topic,
    string Key,
    string EventType,
    string Payload,
    DateTimeOffset OccurredAt);

/// <summary>
/// Transport for outbox rows. An interface so the OutboxDispatcher can be
/// tested without a broker, and so swapping Kafka out is a one-class change.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>Kafka header names shared by every producer and consumer in the platform.</summary>
public static class EventHeaders
{
    public const string MessageId = "message-id";
    public const string EventType = "event-type";
    public const string TenantId = "tenant-id";
    public const string OccurredAt = "occurred-at";
}
