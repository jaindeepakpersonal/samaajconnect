using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Application.Common;
using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Application.Auctions;

/// <summary>
/// Domain to wire, in one place.
/// </summary>
/// <remarks>
/// Two rules are enforced here rather than left to each caller, because both are
/// about what a bidder is allowed to know and a missed copy would be a leak
/// rather than a cosmetic bug:
///
/// - **A bid never carries its member id** except as "is this mine". While a Boli
///   is open, a public list of who is prepared to pay what turns an auction into
///   a statement about people's means.
/// - **A result never carries its winner until it is published**, for anybody at
///   all. The two-step exists so nothing is announced before it is announced.
/// </remarks>
public static class BoliMappings
{
    public static OccasionResponse ToResponse(BoliOccasion occasion, int boliCount) =>
        new(
            occasion.Id,
            occasion.Title,
            occasion.Description,
            occasion.OccasionDate,
            occasion.Status.ToString(),
            occasion.Types.Count,
            boliCount);

    public static BoliTypeResponse ToResponse(BoliType type) =>
        new(type.Id, type.Name, type.Description);

    public static BoliResponse ToResponse(
        Domain.Auctions.Boli lot,
        string typeName,
        DateTimeOffset now,
        long? highest,
        bool highestBidderIsMe,
        int bidCount) =>
        new(
            lot.Id,
            lot.OccasionId,
            lot.BoliTypeId,
            typeName,
            lot.Title,
            lot.StartAt,
            lot.EndAt,
            lot.StartingAmount,
            lot.MinIncrement,
            lot.AutoExtendSeconds,
            lot.EligibilityRule,
            lot.Status.ToString(),
            lot.AcceptsBids(now),
            highest,
            lot.MinimumNextBid(highest),
            highestBidderIsMe,
            bidCount);

    public static BidResponse ToResponse(Bid bid, Guid? currentMemberId) =>
        new(bid.Id, bid.Amount, bid.PlacedAt, bid.MemberId == currentMemberId);

    public static BoliResultResponse ToResponse(
        BoliResult result, Domain.Auctions.Boli lot, Guid? currentMemberId) =>
        new(
            lot.Id,
            lot.Title,
            result.Amount,
            // Null until published, for everybody. A shape that carries the
            // winner "but only to the right caller" is one authorization mistake
            // away from announcing it early.
            result.IsPublished ? result.WinningMemberId : null,
            result.WinningMemberId == currentMemberId && result.IsPublished,
            result.IsPublished,
            result.RecordedAt,
            result.PublishedAt);

    /// <summary>
    /// Describes a Boli with its live bidding state, which needs two more reads.
    /// </summary>
    public static async Task<Result<BoliResponse>> DescribeAsync(
        Domain.Auctions.Boli lot,
        IOccasionRepository occasions,
        IBoliRepository boli,
        Guid? currentMemberId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var occasion = await occasions.GetByIdAsync(lot.OccasionId, cancellationToken);
        var typeName = occasion?.FindType(lot.BoliTypeId)?.Name ?? string.Empty;

        var bids = await boli.ListBidsAsync(lot.Id, cancellationToken);
        var top = bids.Count > 0 ? bids[0] : null;

        return Result.Success(ToResponse(
            lot,
            typeName,
            now,
            top?.Amount,
            top is not null && top.MemberId == currentMemberId,
            bids.Count));
    }
}
