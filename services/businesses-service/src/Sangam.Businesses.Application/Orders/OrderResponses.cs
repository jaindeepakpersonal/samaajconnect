namespace Sangam.Businesses.Application.Orders;

public sealed record OrderItemResponse(Guid ProductId, string ProductName, int UnitPriceInPaise, int Quantity);

/// <summary>
/// An order as either the buyer or the business owner sees it.
/// <see cref="BusinessName"/> is resolved by the handler at read time, not
/// stored on the order itself - a member who owns several businesses needs
/// to tell rows apart on the Received tab, the same "resolve it once, in the
/// response" shape Matrimony's `ProposalResponse.OtherParty` already uses.
/// </summary>
public sealed record OrderResponse(
    Guid Id,
    Guid BusinessId,
    string BusinessName,
    Guid BuyerMemberId,
    string Status,
    IReadOnlyList<OrderItemResponse> Items,
    int TotalAmountInPaise,
    string? Note,
    DateTimeOffset PlacedAt,
    DateTimeOffset? RespondedAt,
    DateTimeOffset? ClosedAt);
