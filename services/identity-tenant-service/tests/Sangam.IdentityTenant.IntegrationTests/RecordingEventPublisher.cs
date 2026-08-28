using System.Collections.Concurrent;
using Sangam.IdentityTenant.Infrastructure.Messaging;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Stands in for Kafka. The Outbox pattern's contract is "the row exists if and
/// only if the transaction committed" — proving that needs a real database, not
/// a real broker, so the broker is the one thing these tests fake.
/// </summary>
public sealed class RecordingEventPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<PublishedEvent> _published = new();

    /// <summary>When set, every publish throws — used to exercise the retry path.</summary>
    public bool ShouldFail { get; set; }

    public IReadOnlyCollection<PublishedEvent> Published => _published.ToArray();

    public Task PublishAsync(
        string topic,
        string key,
        string payload,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Simulated broker failure.");
        }

        _published.Enqueue(new PublishedEvent(topic, key, payload));

        return Task.CompletedTask;
    }

    public void Clear() => _published.Clear();

    public sealed record PublishedEvent(string Topic, string Key, string Payload);
}
