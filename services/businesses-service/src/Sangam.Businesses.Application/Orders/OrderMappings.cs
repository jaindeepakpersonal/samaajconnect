using Sangam.Businesses.Domain.Orders;

namespace Sangam.Businesses.Application.Orders;

public static class OrderMappings
{
    /// <summary>
    /// A business that no longer exists (fully erased along with its owner's
    /// account) still names an order - "A business no longer on the
    /// platform" is what the caller passes for <paramref name="businessName"/>
    /// in that case, the same "this member is no longer on the platform"
    /// fallback Matrimony's own Proposals screen already uses.
    /// </summary>
    public static OrderResponse ToResponse(this Order order, string businessName) => new(
        order.Id,
        order.BusinessId,
        businessName,
        order.BuyerMemberId,
        order.Status.ToString(),
        [.. order.Items.Select(i => new OrderItemResponse(i.ProductId, i.ProductName, i.UnitPriceInPaise, i.Quantity))],
        order.TotalAmountInPaise,
        order.Note,
        order.PlacedAt,
        order.RespondedAt,
        order.ClosedAt);
}
