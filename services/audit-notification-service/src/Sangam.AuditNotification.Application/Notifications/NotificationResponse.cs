namespace Sangam.AuditNotification.Application.Notifications;

public sealed record NotificationResponse(
    Guid Id,
    string Title,
    string Body,
    string Channel,
    string Status,
    bool IsBroadcast,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);
