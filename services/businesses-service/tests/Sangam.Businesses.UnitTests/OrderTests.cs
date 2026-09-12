using FluentAssertions;
using Sangam.Businesses.Domain.Orders;
using Sangam.Businesses.Domain.Orders.Events;
using Xunit;

namespace Sangam.Businesses.UnitTests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BusinessId = Guid.NewGuid();
    private static readonly Guid OwnerMemberId = Guid.NewGuid();
    private static readonly Guid BuyerMemberId = Guid.NewGuid();

    private static IReadOnlyList<OrderItem> Items() =>
        [new OrderItem(Guid.NewGuid(), "Kaju Katli", 45000, 2)];

    private static Order Placed() =>
        Order.Place(TenantId, BusinessId, OwnerMemberId, BuyerMemberId, Items(), "Please pack well.", Now);

    [Fact]
    public void Placing_computes_the_total_from_the_line_items()
    {
        var order = Placed();

        order.TotalAmountInPaise.Should().Be(90000);
        order.Status.Should().Be(OrderStatus.Pending);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderPlacedDomainEvent>();
    }

    [Fact]
    public void A_member_cannot_order_from_their_own_business()
    {
        var act = () => Order.Place(TenantId, BusinessId, OwnerMemberId, OwnerMemberId, Items(), null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_order_needs_at_least_one_item()
    {
        var act = () => Order.Place(TenantId, BusinessId, OwnerMemberId, BuyerMemberId, [], null, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_owner_can_accept_a_pending_order()
    {
        var order = Placed();
        order.ClearDomainEvents();

        order.Accept(OwnerMemberId, Now.AddHours(1));

        order.Status.Should().Be(OrderStatus.Accepted);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderRespondedDomainEvent>();
    }

    [Fact]
    public void Only_the_owner_may_accept()
    {
        var order = Placed();

        var act = () => order.Accept(BuyerMemberId, Now.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_owner_can_decline_a_pending_order()
    {
        var order = Placed();

        order.Decline(OwnerMemberId, Now.AddHours(1));

        order.Status.Should().Be(OrderStatus.Declined);
    }

    [Fact]
    public void A_decided_order_cannot_be_accepted_again()
    {
        var order = Placed();
        order.Accept(OwnerMemberId, Now.AddHours(1));

        var act = () => order.Accept(OwnerMemberId, Now.AddHours(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_owner_can_fulfill_an_accepted_order()
    {
        var order = Placed();
        order.Accept(OwnerMemberId, Now.AddHours(1));
        order.ClearDomainEvents();

        order.Fulfill(OwnerMemberId, Now.AddHours(2));

        order.Status.Should().Be(OrderStatus.Fulfilled);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderFulfilledDomainEvent>();
    }

    [Fact]
    public void A_pending_order_cannot_be_fulfilled()
    {
        var order = Placed();

        var act = () => order.Fulfill(OwnerMemberId, Now.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_buyer_can_cancel_while_pending()
    {
        var order = Placed();
        order.ClearDomainEvents();

        order.Cancel(BuyerMemberId, Now.AddHours(1));

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OrderCancelledDomainEvent>();
    }

    [Fact]
    public void The_buyer_cannot_cancel_an_accepted_order()
    {
        var order = Placed();
        order.Accept(OwnerMemberId, Now.AddHours(1));

        var act = () => order.Cancel(BuyerMemberId, Now.AddHours(2));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_owner_can_cancel_an_accepted_order()
    {
        var order = Placed();
        order.Accept(OwnerMemberId, Now.AddHours(1));

        order.Cancel(OwnerMemberId, Now.AddHours(2));

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public void The_owner_cannot_cancel_a_pending_order()
    {
        var order = Placed();

        // While Pending, only the buyer's own withdrawal path applies - the
        // owner's cancel window opens once they have accepted it.
        var act = () => order.Cancel(OwnerMemberId, Now.AddHours(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Deidentifying_the_buyer_strips_the_note_but_keeps_the_outcome()
    {
        var order = Placed();
        order.Accept(OwnerMemberId, Now.AddHours(1));

        order.DeIdentifyBuyer();

        order.Note.Should().BeNull();
        order.Status.Should().Be(OrderStatus.Accepted);
    }
}
