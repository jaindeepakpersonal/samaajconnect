using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;
using Sangam.Businesses.Domain.Orders;

namespace Sangam.Businesses.Application.Orders.Commands.FulfillOrder;

/// <summary>The business owner marks an accepted order fulfilled. Terminal - a fulfilled order cannot be cancelled afterwards.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record FulfillOrderCommand(Guid OrderId) : ICommand<OrderResponse>;

public sealed class FulfillOrderCommandHandler(
    IOrderRepository orders,
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<FulfillOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(FulfillOrderCommand command, CancellationToken cancellationToken)
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

        if (order.Status != OrderStatus.Accepted)
        {
            return Result.Failure<OrderResponse>(Error.Conflict(
                "Order.NotAccepted", "Only an accepted order can be marked fulfilled."));
        }

        order.Fulfill(memberId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var business = await businesses.GetByIdAsync(order.BusinessId, cancellationToken);

        return Result.Success(order.ToResponse(business?.Name ?? "A business no longer on the platform"));
    }
}
