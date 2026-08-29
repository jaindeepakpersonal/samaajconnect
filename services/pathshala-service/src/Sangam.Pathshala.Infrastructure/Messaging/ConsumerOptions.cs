namespace Sangam.Pathshala.Infrastructure.Messaging;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    /// <summary>
    /// The one topic this service acts on, named explicitly rather than matched
    /// by pattern.
    /// </summary>
    /// <remarks>
    /// SERVICES.md names <c>members.child-conversion.approved.v1</c>. That event
    /// is published when an admin approves the conversion, before
    /// identity-tenant-service has created anything, so it carries a child
    /// profile id and no user id - there is nothing for this service to link an
    /// enrolment to. The completed event carries both, and is published at the
    /// moment the link becomes true.
    ///
    /// Subscribing by pattern would mean quietly committing offsets for every
    /// event this service has no handler for.
    /// </remarks>
    public string[] Topics { get; set; } = ["identity.child-conversion.completed.v1"];

    public string GroupId { get; set; } = "pathshala-service";

    /// <summary>Attempts per message before it is logged at Critical and skipped.</summary>
    public int MaxAttempts { get; set; } = 5;

    public int RetryDelayMilliseconds { get; set; } = 500;

    /// <summary>
    /// How often to re-read broker metadata. Confluent defaults to five
    /// minutes, which is a long time for a newly created topic to go unnoticed.
    /// </summary>
    public int MetadataRefreshIntervalMilliseconds { get; set; } = 30_000;
}
