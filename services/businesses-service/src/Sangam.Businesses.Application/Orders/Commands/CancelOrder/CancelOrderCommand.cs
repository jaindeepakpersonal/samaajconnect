using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;
using Sangam.Businesses.Domain.Orders;

namespace Sangam.Businesses.Application.Orders.Commands.CancelOrder;

/// <summary>
/// The buyer cancels while still Pending, or the business owner cancels an
/// Accepted order they have decided not to fulfill - the same
/// sender-only-while-Pending shape Matrimony's own Withdraw already uses,
/// extended with the owner's own later cancel window. Refused as not-found
/// to anyone who is neither party.
/// </summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record CancelOrderCommand(Guid OrderId) : ICommand<OrderResponse>;

public sealed class CancelOrderCommandHandler(
    IOrderRepository orders,
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CancelOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<OrderResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var order = await orders.GetByIdAsync(command.OrderId, cancellationToken);

        if (order is null || (order.BuyerMemberId != memberId && order.BusinessOwnerMemberId != memberId))
        {
            return Result.Failure<OrderResponse>(Error.NotFound("Order.NotFound", "No such order."));
        }

        if (tenantContext.TenantId is { } tenantId && order.TenantId != tenantId)
        {
            return Result.Failure<OrderResponse>(Error.NotFound("Order.NotFound", "No such order."));
        }

        var isBuyer = order.BuyerMemberId == memberId;
        var requiredStatus = isBuyer ? OrderStatus.Pending : OrderStatus.Accepted;

        if (order.Status != requiredStatus)
        {
            return Result.Failure<OrderResponse>(Error.Conflict(
                "Order.CannotCancel",
                isBuyer
                    ? "A pending order can be cancelled; this one has already moved on."
                    : "An accepted order can be cancelled; this one has already moved on."));
        }

        order.Cancel(memberId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var business = await businesses.GetByIdAsync(order.BusinessId, cancellationToken);

        return Result.Success(order.ToResponse(business?.Name ?? "A business no longer on the platform"));
    }
}
