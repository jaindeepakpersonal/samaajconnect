namespace Sangam.Boli.Application.Auctions;

/// <summary>An occasion as the list shows it.</summary>
public sealed record OccasionResponse(
    Guid Id,
    string Title,
    string? Description,
    DateOnly OccasionDate,
    string Status,
    int TypeCount,
    int BoliCount);

public sealed record OccasionDetailResponse(
    Guid Id,
    string Title,
    string? Description,
    DateOnly OccasionDate,
    string Status,
    IReadOnlyList<BoliTypeResponse> Types,
    IReadOnlyList<BoliResponse> Boli);

public sealed record BoliTypeResponse(Guid Id, string Name, string? Description);

/// <summary>
/// One Boli, as a bidder sees it.
/// </summary>
/// <remarks>
/// <paramref name="HighestAmount"/> is the number the screen leads with and
/// <paramref name="MinimumNextBid"/> is what it must put in the input's
/// placeholder — computed here rather than on the client, because the increment
/// rule belongs to the Boli and a client that computes its own would be a second
/// copy of it that can drift.
///
/// <paramref name="HighestBidderIsMe"/> rather than the winning member's id:
/// **the wireframe hides who is leading until the Boli closes** ("name hidden
/// until close"), and a bidder still needs to know whether the bid they are
/// looking at is their own. A boolean answers that without naming anybody.
/// </remarks>
public sealed record BoliResponse(
    Guid Id,
    Guid OccasionId,
    Guid BoliTypeId,
    string BoliTypeName,
    string Title,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    long StartingAmount,
    long MinIncrement,
    string? EligibilityRule,
    string Status,

    // Taking bids right now: the status says so and the clock agrees.
    bool AcceptsBids,
    long? HighestAmount,
    long MinimumNextBid,
    bool HighestBidderIsMe,
    int BidCount);

/// <summary>
/// One row of the wireframe's bid history: an amount and a time.
/// </summary>
/// <remarks>
/// <paramref name="IsMine"/> and no member id. While a Boli is open, who bid
/// what is not the Samaaj's business — a public running list of who is prepared
/// to pay what turns an auction into a statement about people's means. After it
/// closes the winner is announced, and only the winner.
/// </remarks>
public sealed record BidResponse(Guid Id, long Amount, DateTimeOffset PlacedAt, bool IsMine);

/// <summary>
/// What placing a bid answers with.
/// </summary>
/// <remarks>
/// <paramref name="Accepted"/> is false when the amount did not clear the bar,
/// which is reported as success rather than as an error: somebody outbid while
/// the form was open has not done anything wrong, and the response carries the
/// number they now need. A screen that shows a red error for that is telling a
/// bidder off for being slow.
/// </remarks>
public sealed record PlaceBidResponse(
    Guid BoliId,
    Guid? BidId,
    bool Accepted,
    string? Reason,
    long? HighestAmount,
    long MinimumNextBid);

/// <summary>
/// A result. Who won is present only once it has been published.
/// </summary>
/// <remarks>
/// <paramref name="WinningMemberId"/> is null on a recorded-but-unpublished
/// result for everyone, including the manager who recorded it — the point of the
/// two steps is that nothing is announced until it is announced, and a response
/// shape that carries the winner "but only to the right caller" is one
/// authorization mistake away from announcing it.
/// </remarks>
public sealed record BoliResultResponse(
    Guid BoliId,
    string BoliTitle,
    long Amount,
    Guid? WinningMemberId,
    bool WinnerIsMe,
    bool IsPublished,
    DateTimeOffset RecordedAt,
    DateTimeOffset? PublishedAt);
