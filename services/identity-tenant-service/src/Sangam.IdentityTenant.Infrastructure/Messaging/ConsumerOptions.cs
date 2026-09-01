namespace Sangam.IdentityTenant.Infrastructure.Messaging;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>
    /// The topics this service acts on, named explicitly rather than matched by
    /// pattern. This service publishes far more than it consumes; the one thing
    /// it reacts to is a Samaaj admin approving an adult-child conversion,
    /// because only identity can create the account that implies.
    /// </summary>
    public string[] Topics { get; set; } = ["members.child-conversion.approved.v1"];

    /// <summary>
    /// This service's Kafka consumer group. Defined here and deliberately
    /// <b>not</b> in appsettings.json.
    ///
    /// Six services once carried "timeline-service" in their config, having
    /// been scaffolded from it, and the only reason nothing broke was that
    /// just one of them actually ran a consumer. Kafka gives each message in a
    /// group to exactly one member, so the moment a second service joined,
    /// events would have been delivered to a service that ignores them,
    /// committed, and lost - intermittently, by partition assignment. Keeping
    /// the value here, beside the topic list, means a copied appsettings
    /// cannot reintroduce it.
    /// </summary>
    public string GroupId { get; set; } = "identity-tenant-service";

    /// <summary>Attempts per message before it is logged at Critical and skipped.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int RetryDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// How often to re-read broker metadata. Confluent defaults to five
    /// minutes, which is a long time for a newly created topic to go unnoticed.
    /// </summary>
    public int MetadataRefreshIntervalMilliseconds { get; set; } = 30_000;
}
