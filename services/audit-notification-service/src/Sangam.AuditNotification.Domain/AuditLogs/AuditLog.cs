using Sangam.AuditNotification.Domain.Common;

namespace Sangam.AuditNotification.Domain.AuditLogs;

/// <summary>
/// One recorded fact about something that happened on the platform.
/// </summary>
/// <remarks>
/// Append-only by construction: every property has a private setter, there is
/// no mutating method, and the service exposes no update or delete endpoint.
/// SECURITY-CHECKLIST.md requires that audit rows are immutable "ever", so the
/// absence of a way to change one is the feature.
/// </remarks>
public sealed class AuditLog : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>
    /// Id of the outbox row this was built from. Unique per tenant, which is
    /// what makes replay handling a database guarantee rather than a hope -
    /// delivery is at-least-once, so the same event will arrive twice.
    /// </summary>
    public Guid SourceMessageId { get; private set; }

    /// <summary>Kafka topic the event arrived on, e.g. identity.user.registered.v1.</summary>
    public string Topic { get; private set; } = null!;

    /// <summary>CLR type name of the originating domain event.</summary>
    public string EventType { get; private set; } = null!;

    /// <summary>Verb-ish summary of what happened, e.g. UserRegistered.</summary>
    public string Action { get; private set; } = null!;

    public Guid? ActorUserId { get; private set; }
    public string? ActorRole { get; private set; }
    public string? EntityName { get; private set; }
    public string? EntityId { get; private set; }

    /// <summary>The event payload as received, kept verbatim so the row is self-describing.</summary>
    public string AfterState { get; private set; } = null!;

    public string? BeforeState { get; private set; }
    public string? IpAddress { get; private set; }

    /// <summary>When the thing happened, per the publishing service.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>When this service wrote the row. Differs from OccurredAt after a backlog.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    private AuditLog() { }

    public static AuditLog FromEvent(
        Guid tenantId,
        Guid sourceMessageId,
        string topic,
        string eventType,
        string action,
        string payload,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        Guid? actorUserId = null,
        string? actorRole = null,
        string? entityName = null,
        string? entityId = null,
        string? beforeState = null,
        string? ipAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        return new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SourceMessageId = sourceMessageId,
            Topic = topic,
            EventType = eventType,
            Action = action,
            AfterState = payload,
            OccurredAt = occurredAt,
            RecordedAt = recordedAt,
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            EntityName = entityName,
            EntityId = entityId,
            BeforeState = beforeState,
            IpAddress = ipAddress,
        };
    }
}
