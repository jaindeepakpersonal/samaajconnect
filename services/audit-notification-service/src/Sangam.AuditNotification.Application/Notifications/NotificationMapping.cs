using Sangam.AuditNotification.Application.Abstractions;

namespace Sangam.AuditNotification.Application.Notifications;

/// <summary>
/// The one place a notification becomes a response.
/// </summary>
/// <remarks>
/// Shared by the member's notification list and the DPDP s.11 export, which
/// previously each had their own copy of this mapping. That was survivable while
/// the two agreed; it stopped being survivable when read state moved off the
/// notification, because one copy would have kept reporting a field that is no
/// longer there and nothing would have failed.
/// </remarks>
public static class NotificationMapping
{
    public static NotificationResponse ToResponse(MemberNotification row) => new(
        row.Notification.Id,
        row.Notification.Title,
        row.Notification.Body,
        row.Notification.Channel.ToString(),
        row.Notification.Status.ToString(),
        row.Notification.RecipientUserId is null,
        row.Notification.CreatedAt,
        row.ReadAt,
        row.Notification.Destination);
}
