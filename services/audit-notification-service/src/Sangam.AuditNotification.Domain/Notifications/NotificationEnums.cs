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

    /// <summary>
    /// Claimed by a dispatcher and handed to a channel, outcome not yet known.
    /// A row sitting here is either in flight or was abandoned by a process
    /// that died mid-send; <see cref="Notification.ReleaseStalledClaim"/> is
    /// what tells those two apart, after a timeout.
    /// </summary>
    Sending = 5,
}
