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

    // 4 was Read, and it is gone rather than reused. Read-ness is a fact about
    // a person and a message - see NotificationRead - and having it in here
    // made this enum two state machines wearing one name, which is how a
    // broadcast came to be markable as read for an entire Samaaj at once.
    // Left as a gap so an old row deserialised from somewhere fails loudly
    // rather than silently becoming whatever 4 means next.

    /// <summary>
    /// Claimed by a dispatcher and handed to a channel, outcome not yet known.
    /// A row sitting here is either in flight or was abandoned by a process
    /// that died mid-send; <see cref="Notification.ReleaseStalledClaim"/> is
    /// what tells those two apart, after a timeout.
    /// </summary>
    Sending = 5,
}
