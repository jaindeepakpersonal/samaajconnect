namespace Sangam.MasterData.Domain.Common;

/// <summary>
/// A fact that has already happened inside the domain. Raised by an aggregate,
/// persisted to the Outbox in the same transaction as the state change, and
/// published to Kafka by the OutboxDispatcher (root CLAUDE.md §5).
/// </summary>
public interface IDomainEvent
{
    /// <summary>Kafka topic this event is published to.</summary>
    string Topic { get; }

    /// <summary>
    /// Tenant this event belongs to, used as the Kafka partition key so all
    /// events for one Samaaj stay ordered relative to each other.
    /// </summary>
    /// <remarks>
    /// <see cref="Guid.Empty"/> for the platform-wide lookups (Country, City,
    /// Locality, ProfessionType, Gender, MaritalStatus) - they belong to no
    /// Samaaj, the same sentinel <c>PlatformClaimTypes.TenantId</c> already
    /// uses for a platform account's token. Only <c>Gautra</c> events carry a
    /// real tenant id.
    /// </remarks>
    Guid TenantId { get; }

    DateTimeOffset OccurredAt { get; }
}
