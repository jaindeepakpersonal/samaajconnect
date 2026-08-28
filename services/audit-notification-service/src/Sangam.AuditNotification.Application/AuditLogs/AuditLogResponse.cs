namespace Sangam.AuditNotification.Application.AuditLogs;

public sealed record AuditLogResponse(
    Guid Id,
    Guid TenantId,
    string Action,
    string EntityName,
    string? EntityId,
    Guid? ActorUserId,
    string Topic,
    string EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt);
