namespace Sangam.MasterData.Infrastructure.Persistence;

/// <summary>
/// One domain event, written in the same transaction as the state change that
/// raised it (root CLAUDE.md §5).
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>
    /// Partition key on Kafka. <see cref="Guid.Empty"/> for the platform-wide
    /// lookup, city and locality events - see <c>IDomainEvent.TenantId</c>.
    /// </summary>
    public Guid TenantId { get; init; }

    public string Topic { get; init; } = null!;

    public string Type { get; init; } = null!;

    public string Payload { get; init; } = null!;

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    public string? Error { get; set; }
}
