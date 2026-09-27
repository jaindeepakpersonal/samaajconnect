namespace Sangam.Trust.Infrastructure.Messaging;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>
    /// The one topic this service acts on, named explicitly rather than matched
    /// by pattern: a published Boli result, which opens the amount the winner
    /// owes to the Trust of the occasion's locality.
    /// </summary>
    /// <remarks>
    /// Subscribing by pattern would mean quietly committing offsets for every
    /// event this service has no handler for.
    /// </remarks>
    public string[] Topics { get; set; } = ["boli.result.published.v1"];

    /// <summary>
    /// This service's Kafka consumer group. Defined here and deliberately
    /// <b>not</b> in appsettings.json - see pathshala-service's ConsumerOptions
    /// for the copied-appsettings incident that rule comes from. Kafka gives
    /// each message in a group to exactly one member, so sharing a group with
    /// another service would hand it Boli results it ignores, and lose them.
    /// </summary>
    public string GroupId { get; set; } = "trust-service";

    /// <summary>Attempts per message before it is logged at Critical and skipped.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int RetryDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// How often to re-read broker metadata. Confluent defaults to five
    /// minutes, which is a long time for a newly created topic to go unnoticed.
    /// </summary>
    public int MetadataRefreshIntervalMilliseconds { get; set; } = 30_000;
}
