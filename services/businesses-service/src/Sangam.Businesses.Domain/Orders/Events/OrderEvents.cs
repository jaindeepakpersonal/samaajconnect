using Sangam.Businesses.Domain.Common;

namespace Sangam.Businesses.Domain.Orders.Events;

public sealed record OrderPlacedDomainEvent(
    Guid OrderId,
    Guid TenantId,
    Guid BusinessId,
    Guid BuyerMemberId,
    int TotalAmountInPaise,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "businesses.order.placed.v1";
}

/// <summary>
/// Carries the outcome as a bool - "Accepted" or "Declined" - the same
/// convention `ProposalRespondedDomainEvent` uses for a decision fact on the
/// wire, there as a string since it also covered Withdrawn.
/// `BusinessOwnerMemberId` is the actor here - responding to an order is
/// always the owner's act, unlike Cancel below, which either party can do.
/// </summary>
public sealed record OrderRespondedDomainEvent(
    Guid OrderId,
    Guid TenantId,
    Guid BusinessId,
    Guid BusinessOwnerMemberId,
    Guid BuyerMemberId,
    bool Accepted,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "businesses.order.responded.v1";
}

/// <summary>Fulfilling is always the business owner's own act, so `BusinessOwnerMemberId` is the actor.</summary>
public sealed record OrderFulfilledDomainEvent(
    Guid OrderId,
    Guid TenantId,
    Guid BusinessId,
    Guid BusinessOwnerMemberId,
    Guid BuyerMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "businesses.order.fulfilled.v1";
}

/// <summary>Raised whether the buyer cancelled a Pending order or the owner cancelled an Accepted one - the event names the fact, not who did it, the same shape a Boli's own status events already take.</summary>
public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    Guid TenantId,
    Guid BusinessId,
    Guid BuyerMemberId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "businesses.order.cancelled.v1";
}
