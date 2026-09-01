using Sangam.Boli.Domain.Common;

namespace Sangam.Boli.Domain.Auctions;

/// <summary>
/// One offer on one Boli.
/// </summary>
/// <remarks>
/// Written directly rather than through <see cref="Boli"/>, and never loaded as
/// a collection on it — see that type's remarks for why.
///
/// <b>A bid is never deleted or amended.</b> The bid history is what a Samaaj
/// shows when somebody asks how a Boli went, and a history that can be edited
/// afterwards answers that question with whatever the editor preferred. A bidder
/// who wants out is outbid, not erased.
/// </remarks>
public sealed class Bid : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BoliId { get; private set; }
    public Guid MemberId { get; private set; }

    /// <summary>In paise. See <see cref="Boli.StartingAmount"/>.</summary>
    public long Amount { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    private Bid() { }   // EF Core

    public static Bid Place(
        Guid tenantId, Guid boliId, Guid memberId, long amount, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BoliId = boliId,
            MemberId = memberId,
            Amount = amount,
            PlacedAt = now,
        };
}

/// <summary>
/// Who won a Boli, and for how much.
/// </summary>
/// <remarks>
/// Recorded first and published second, deliberately two steps. Whoever runs the
/// Boli needs to be able to check a result against what happened in the room
/// before the Samaaj sees it, and once the Samaaj has seen it, it is fixed.
/// <see cref="PublishedAt"/> being null is the whole difference between those
/// two states.
/// </remarks>
public sealed class BoliResult : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BoliId { get; private set; }
    public Guid WinningBidId { get; private set; }
    public Guid WinningMemberId { get; private set; }

    /// <summary>In paise, copied from the winning bid so the result stands alone.</summary>
    public long Amount { get; private set; }

    public Guid RecordedBy { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public bool IsPublished => PublishedAt is not null;

    private BoliResult() { }   // EF Core

    public static BoliResult Record(
        Guid tenantId,
        Guid boliId,
        Bid winning,
        Guid recordedBy,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BoliId = boliId,
            WinningBidId = winning.Id,
            WinningMemberId = winning.MemberId,
            Amount = winning.Amount,
            RecordedBy = recordedBy,
            RecordedAt = now,
        };

    /// <summary>
    /// Publishes it. Idempotent: a second publish leaves the first one's
    /// attribution and timestamp alone rather than overwriting them.
    /// </summary>
    /// <remarks>
    /// SERVICES.md requires publishing to be idempotent and irreversible through
    /// the normal API. Returning true on a repeat rather than failing is what
    /// makes a retried request safe; keeping the original <see cref="PublishedBy"/>
    /// is what stops a repeat quietly reassigning who announced it.
    /// </remarks>
    public bool Publish(Guid publishedBy, DateTimeOffset now)
    {
        if (IsPublished)
        {
            return true;
        }

        PublishedBy = publishedBy;
        PublishedAt = now;

        return true;
    }
}
