namespace Sangam.MemberFamily.Infrastructure.Messaging;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>
    /// Only the one topic this service acts on. Narrow on purpose, unlike
    /// audit-notification-service, which subscribes to everything: this
    /// service reacts to registrations, and consuming events it has no
    /// handler for would mean quietly committing offsets for messages it did
    /// nothing with.
    /// </summary>
    public string TopicPattern { get; set; } = "^identity[.]user[.]registered[.]v[0-9]+$";

    public string GroupId { get; set; } = "member-family-service";

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
