using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;
using Sangam.Businesses.Domain.Orders;

namespace Sangam.Businesses.Application.Orders.Commands.RespondToOrder;

/// <summary>The business owner accepts or declines a pending order. Refused as not-found to anyone else.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record RespondToOrderCommand(Guid OrderId, bool Accept) : ICommand<OrderResponse>;

public sealed class RespondToOrderCommandHandler(
    IOrderRepository orders,
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RespondToOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(RespondToOrderCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<OrderResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var order = await orders.GetByIdAsync(command.OrderId, cancellationToken);

        if (order is null || order.BusinessOwnerMemberId != memberId)
        {
            return Result.Failure<OrderResponse>(Error.NotFound("Order.NotFound", "No such order."));
        }

        if (tenantContext.TenantId is { } tenantId && order.TenantId != tenantId)
        {
            return Result.Failure<OrderResponse>(Error.NotFound("Order.NotFound", "No such order."));
        }

        if (order.Status != OrderStatus.Pending)
        {
            return Result.Failure<OrderResponse>(Error.Conflict(
                "Order.AlreadyDecided", "This order has already been decided."));
        }

        var now = clock.UtcNow;

        if (command.Accept)
        {
            order.Accept(memberId, now);
        }
        else
        {
            order.Decline(memberId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var business = await businesses.GetByIdAsync(order.BusinessId, cancellationToken);

        return Result.Success(order.ToResponse(business?.Name ?? "A business no longer on the platform"));
    }
}
