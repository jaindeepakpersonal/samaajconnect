using Sangam.Competition.Domain.Common;

namespace Sangam.Competition.Domain.Entries;

/// <summary>
/// A round's frozen outcome: entry ids in rank order, best first.
/// </summary>
/// <remarks>
/// Stored rather than recomputed, mirroring celebrity-voting-service's own
/// <c>CampaignResult</c>. A ranking recomputed on every read could change
/// after it was announced - by a corrected score, a late judge, a retried
/// write - and an announced result that moves is worse than no result at
/// all.
/// </remarks>
public sealed class RoundResult : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RoundId { get; private set; }

    /// <summary>Ordered best-first. Stored as JSON; the order is the result.</summary>
    public IReadOnlyList<Guid> RankedEntryIds { get; private set; } = [];

    public Guid PublishedBy { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }

    private RoundResult() { }   // EF Core

    public RoundResult(
        Guid tenantId,
        Guid roundId,
        IReadOnlyList<Guid> rankedEntryIds,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        RoundId = roundId;
        RankedEntryIds = rankedEntryIds;
        PublishedBy = publishedBy;
        PublishedAt = publishedAt;
    }
}
