namespace Sangam.AuditNotification.Infrastructure.Messaging;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>
    /// Kafka subscribes by regex when the pattern starts with "^". Subscribing
    /// to every versioned platform topic rather than an explicit list means a
    /// new service's events are audited the day it ships, without a change
    /// here - and a hole in the audit trail is worth avoiding more than the
    /// small cost of auditing an event nobody has taught us about yet.
    /// </summary>
    public string TopicPattern { get; set; } = "^[a-z0-9-]+[.][a-z0-9.-]+[.]v[0-9]+$";

    public string GroupId { get; set; } = "audit-notification-service";

    /// <summary>Attempts per message before it is logged at Critical and skipped.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int RetryDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// How often to re-read broker metadata. Kafka only matches a regex
    /// subscription against topics it knows about, so this is how long a newly
    /// deployed service waits before its events start being audited.
    /// Confluent defaults to five minutes, which is a long hole in the trail.
    /// </summary>
    public int MetadataRefreshIntervalMilliseconds { get; set; } = 30_000;
}
