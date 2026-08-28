namespace Sangam.AuditNotification.Domain.Notifications;

public enum NotificationChannel
{
    InApp = 1,
    Email = 2,
    Sms = 3,
    WhatsApp = 4,
}

public enum NotificationStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Read = 4,
}
