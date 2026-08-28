namespace Sangam.MemberFamily.Domain.Common;

/// <summary>
/// A fact that has already happened inside the domain. Raised by an aggregate,
/// persisted to the Outbox in the same transaction as the state change, and
/// published to Kafka by the OutboxDispatcher (CLAUDE.md §5).
/// </summary>
public interface IDomainEvent
{
    /// <summary>Kafka topic this event is published to.</summary>
    string Topic { get; }

    /// <summary>
    /// Tenant this event belongs to, used as the Kafka partition key so all
    /// events for one Samaaj stay ordered relative to each other.
    /// </summary>
    Guid TenantId { get; }

    DateTimeOffset OccurredAt { get; }
}
