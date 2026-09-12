using Sangam.Businesses.Domain.Common;
using Sangam.Businesses.Domain.Orders.Events;

namespace Sangam.Businesses.Domain.Orders;

/// <summary>
/// An order placed against a <see cref="Businesses.Business"/>. Its own
/// aggregate root, not owned by either party - a peer-to-peer fact between a
/// buyer and a business, the same reasoning that keeps Matrimony's `Proposal`
/// and a Boli's bid history their own records rather than nested under one
/// side.
/// </summary>
/// <remarks>
/// <see cref="BusinessOwnerMemberId"/> is snapshotted at placement time
/// (rather than resolved from <see cref="Businesses.Business"/> on every
/// call) so this aggregate can enforce its own owner-only guards the same
/// way Matrimony's `Proposal.Respond` enforces "the recipient only" - a
/// belt-and-braces check here, in addition to the handler's own 404-not-403
/// guard, rather than a check only the handler could ever make.
/// </remarks>
public sealed class Order : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid BusinessId { get; private set; }

    public Guid BusinessOwnerMemberId { get; private set; }

    public Guid BuyerMemberId { get; private set; }

    public OrderStatus Status { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Computed from the snapshotted line items at placement time. Never trusted from the client.</summary>
    public int TotalAmountInPaise { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? RespondedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    private readonly List<OrderItem> _items = [];

    public IReadOnlyList<OrderItem> Items => _items.AsReadOnly();

    private Order() { }

    public static Order Place(
        Guid tenantId,
        Guid businessId,
        Guid businessOwnerMemberId,
        Guid buyerMemberId,
        IReadOnlyList<OrderItem> items,
        string? note,
        DateTimeOffset now)
    {
        if (buyerMemberId == businessOwnerMemberId)
        {
            throw new ArgumentException("You cannot order from your own business.", nameof(buyerMemberId));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("An order needs at least one item.", nameof(items));
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BusinessId = businessId,
            BusinessOwnerMemberId = businessOwnerMemberId,
            BuyerMemberId = buyerMemberId,
            Status = OrderStatus.Pending,
            Note = Normalize(note),
            TotalAmountInPaise = items.Sum(i => i.UnitPriceInPaise * i.Quantity),
            PlacedAt = now,
        };

        order._items.AddRange(items);
        order.Raise(new OrderPlacedDomainEvent(
            order.Id, tenantId, businessId, buyerMemberId, order.TotalAmountInPaise, now));

        return order;
    }

    public void Accept(Guid actingMemberId, DateTimeOffset now)
    {
        RequireOwner(actingMemberId);
        RequireStatus(OrderStatus.Pending);

        Status = OrderStatus.Accepted;
        RespondedAt = now;
        Raise(new OrderRespondedDomainEvent(Id, TenantId, BusinessId, BusinessOwnerMemberId, BuyerMemberId, true, now));
    }

    public void Decline(Guid actingMemberId, DateTimeOffset now)
    {
        RequireOwner(actingMemberId);
        RequireStatus(OrderStatus.Pending);

        Status = OrderStatus.Declined;
        RespondedAt = now;
        ClosedAt = now;
        Raise(new OrderRespondedDomainEvent(Id, TenantId, BusinessId, BusinessOwnerMemberId, BuyerMemberId, false, now));
    }

    public void Fulfill(Guid actingMemberId, DateTimeOffset now)
    {
        RequireOwner(actingMemberId);
        RequireStatus(OrderStatus.Accepted);

        Status = OrderStatus.Fulfilled;
        ClosedAt = now;
        Raise(new OrderFulfilledDomainEvent(Id, TenantId, BusinessId, BusinessOwnerMemberId, BuyerMemberId, now));
    }

    /// <summary>
    /// The buyer cancels while still Pending - mirroring Matrimony's
    /// sender-only Withdraw-while-Pending - or the owner cancels an Accepted
    /// order they have decided not to fulfill. Declined and Fulfilled are
    /// both terminal; nobody cancels an order from there.
    /// </summary>
    public void Cancel(Guid actingMemberId, DateTimeOffset now)
    {
        if (actingMemberId == BuyerMemberId)
        {
            RequireStatus(OrderStatus.Pending);
        }
        else if (actingMemberId == BusinessOwnerMemberId)
        {
            RequireStatus(OrderStatus.Accepted);
        }
        else
        {
            throw new InvalidOperationException("Only the buyer or the business owner may cancel this order.");
        }

        Status = OrderStatus.Cancelled;
        ClosedAt = now;
        Raise(new OrderCancelledDomainEvent(Id, TenantId, BusinessId, BuyerMemberId, now));
    }

    /// <summary>
    /// Strips the buyer's own note when their account is erased. Keeps the
    /// row and every other field, including the outcome, so the business
    /// owner's own order history is not silently broken - the same
    /// "keep the row, strip the person" shape Matrimony's `Proposal.DeIdentify`
    /// already uses.
    /// </summary>
    public void DeIdentifyBuyer() => Note = null;

    private void RequireOwner(Guid actingMemberId)
    {
        if (actingMemberId != BusinessOwnerMemberId)
        {
            throw new InvalidOperationException("Only the business owner may do this.");
        }
    }

    private void RequireStatus(OrderStatus required)
    {
        if (Status != required)
        {
            throw new InvalidOperationException($"This order is not {required}.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// One line of an order. Product name and price are snapshotted here at
/// placement time, not read live off <see cref="Businesses.Product"/>
/// afterwards - an order is a record of what was actually agreed, and an
/// owner editing a price next week must not silently rewrite last week's
/// order.
/// </summary>
public sealed record OrderItem(Guid ProductId, string ProductName, int UnitPriceInPaise, int Quantity);
