namespace Sangam.IdentityTenant.Infrastructure.Messaging;

/// <summary>
/// Transport for outbox rows. An interface so the OutboxDispatcher can be
/// tested without a broker, and so swapping Kafka out is a one-class change.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(string topic, string key, string payload, CancellationToken cancellationToken = default);
}
