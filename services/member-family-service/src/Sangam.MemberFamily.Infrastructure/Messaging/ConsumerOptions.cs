namespace Sangam.MemberFamily.Infrastructure.Messaging;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>
    /// The topics this service acts on, named explicitly.
    /// </summary>
    /// <remarks>
    /// A list rather than the regex audit-notification-service uses, for two
    /// reasons. This service <i>acts</i> on what it consumes, so subscribing to
    /// anything it has no handler for would mean quietly committing offsets for
    /// messages it did nothing with. And librdkafka's regex support is not the
    /// full grammar: a pattern with alternation silently matched nothing here,
    /// which looks exactly like a broker problem and is not one. An explicit
    /// list cannot fail that way.
    /// </remarks>
    public string[] Topics { get; set; } =
    [
        "identity.user.registered.v1",
        "identity.child-conversion.completed.v1",
        "identity.user.erased.v1",
    ];

    public string GroupId { get; set; } = "member-family-service";

    /// <summary>Attempts per message before it is logged at Critical and skipped.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int RetryDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// How often to re-read broker metadata. Confluent defaults to five
    /// minutes, which is a long time for a topic that has just been created to
    /// go unnoticed.
    /// </summary>
    public int MetadataRefreshIntervalMilliseconds { get; set; } = 30_000;
}
