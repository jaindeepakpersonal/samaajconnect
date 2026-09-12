namespace Sangam.Businesses.Infrastructure.Messaging;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>
    /// The topics this service acts on, named explicitly - just one: a
    /// member's full account erasure must take their biodata and proposals
    /// with it. A list rather than a regex, for the same reasons
    /// member-family-service's own consumer states: this service acts on
    /// what it consumes, so subscribing to anything with no handler would
    /// mean quietly committing offsets for messages it did nothing with.
    /// </summary>
    public string[] Topics { get; set; } =
    [
        "identity.user.erased.v1",
    ];

    /// <summary>
    /// This service's Kafka consumer group. Defined here and deliberately
    /// not in appsettings.json - see member-family-service's own
    /// `ConsumerOptions` for the exact incident (six services once carried
    /// another service's group id from being scaffolded off it) this
    /// guards against.
    /// </summary>
    public string GroupId { get; set; } = "businesses-service";

    public int MaxAttempts { get; set; } = 5;

    public int RetryDelayMilliseconds { get; set; } = 500;

    public int MetadataRefreshIntervalMilliseconds { get; set; } = 30_000;
}
