namespace Sangam.AuditNotification.Infrastructure.Notifications;

public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "NotificationDelivery";

    /// <summary>
    /// Turns the dispatcher off. Pending notifications accumulate rather than
    /// being lost, so it is safe to switch off while a provider is misbehaving.
    /// Integration tests set it false so a background loop is not competing with
    /// their assertions over the same rows.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 10;

    public int BatchSize { get; set; } = 25;

    /// <summary>
    /// How long a notification may sit in Sending before it is assumed the
    /// process that claimed it died, and it is returned to the queue.
    /// </summary>
    /// <remarks>
    /// Must be comfortably longer than the slowest send. Set it too low and a
    /// provider that is merely slow gets asked to deliver the same message a
    /// second time while the first is still in flight.
    /// </remarks>
    public int StalledAfterMinutes { get; set; } = 5;

    public LoggingChannelOptions Logging { get; set; } = new();
}

public sealed class LoggingChannelOptions
{
    /// <summary>
    /// Writes the full destination and message body to the log instead of a
    /// redacted summary. Off by default, and it should stay off anywhere the
    /// logs are kept or shipped.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the flag exists rather than the adapter simply
    /// logging everything: the body of a notification is addressed to one
    /// person and the destination identifies them, so a log of both is a copy
    /// of personal data in the one place erasure cannot reach. Turning it on is
    /// for reading an activation code off a local console, and it announces
    /// itself in the log at startup so it cannot be left on unnoticed.
    /// </remarks>
    public bool RevealContent { get; set; }
}
