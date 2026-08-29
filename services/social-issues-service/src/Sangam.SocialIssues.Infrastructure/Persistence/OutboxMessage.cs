namespace Sangam.SocialIssues.Infrastructure.Persistence;

/// <summary>
/// One domain event, written in the same transaction as the state change that
/// raised it (CLAUDE.md §5). The row existing is the guarantee that the event
/// will eventually be published; the OutboxDispatcher is what makes it so.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>Partition key on Kafka, so one Samaaj's events stay mutually ordered.</summary>
    public Guid TenantId { get; init; }

    public string Topic { get; init; } = null!;

    /// <summary>CLR type name of the event, carried so consumers can deserialize.</summary>
    public string Type { get; init; } = null!;

    public string Payload { get; init; } = null!;

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Publish attempts so far. Used to back off and to surface poison messages.</summary>
    public int Attempts { get; set; }

    /// <summary>Last publish error, kept for diagnosis of a stuck row.</summary>
    public string? Error { get; set; }
}
