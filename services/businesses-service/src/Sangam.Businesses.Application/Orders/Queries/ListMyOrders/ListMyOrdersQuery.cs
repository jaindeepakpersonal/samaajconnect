using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Orders.Queries.ListMyOrders;

/// <summary>Orders the caller has placed, against any business.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record ListMyOrdersQuery : IQuery<IReadOnlyList<OrderResponse>>;

public sealed class ListMyOrdersQueryHandler(
    IOrderRepository orders, IBusinessRepository businesses, ICurrentUser currentUser)
    : IRequestHandler<ListMyOrdersQuery, Result<IReadOnlyList<OrderResponse>>>
{
    public async Task<Result<IReadOnlyList<OrderResponse>>> Handle(
        ListMyOrdersQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<OrderResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var placed = await orders.ListPlacedAsync(memberId, cancellationToken);
        var responses = new List<OrderResponse>(placed.Count);

        foreach (var order in placed)
        {
            var business = await businesses.GetByIdAsync(order.BusinessId, cancellationToken);

            responses.Add(order.ToResponse(business?.Name ?? "A business no longer on the platform"));
        }

        return Result.Success<IReadOnlyList<OrderResponse>>(responses);
    }
}
