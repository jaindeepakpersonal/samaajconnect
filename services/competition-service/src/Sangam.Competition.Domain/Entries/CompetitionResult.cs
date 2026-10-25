using Sangam.Competition.Domain.Common;

namespace Sangam.Competition.Domain.Entries;

/// <summary>
/// A competition's frozen final standings: member ids, best first.
/// </summary>
/// <remarks>
/// <see cref="RoundResult"/>'s shape one level up, and stored for the same
/// reason: an announced result must not move. It is published only once
/// every main round is closed, and a closed round takes no more marks, so the
/// totals shown beside this order are re-added on every read and cannot
/// disagree with it.
/// </remarks>
public sealed class CompetitionResult : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CompetitionId { get; private set; }

    /// <summary>Ordered best-first. Stored as JSON; the order is the result.</summary>
    public IReadOnlyList<Guid> RankedMemberIds { get; private set; } = [];

    public Guid PublishedBy { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }

    private CompetitionResult() { }   // EF Core

    public CompetitionResult(
        Guid tenantId,
        Guid competitionId,
        IReadOnlyList<Guid> rankedMemberIds,
        Guid publishedBy,
        DateTimeOffset publishedAt)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        CompetitionId = competitionId;
        RankedMemberIds = rankedMemberIds;
        PublishedBy = publishedBy;
        PublishedAt = publishedAt;
    }
}
