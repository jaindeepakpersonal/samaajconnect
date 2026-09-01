namespace Sangam.AuditNotification.Application.Notifications;

/// <param name="Destination">
/// Where an outbound message was sent - null for in-app, which is addressed by
/// user id. Present so the DPDP s.11 export is complete about what this service
/// holds; the member notification list returns in-app rows only, so it is always
/// null there.
/// </param>
public sealed record NotificationResponse(
    Guid Id,
    string Title,
    string Body,
    string Channel,
    string Status,
    bool IsBroadcast,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? Destination = null);
