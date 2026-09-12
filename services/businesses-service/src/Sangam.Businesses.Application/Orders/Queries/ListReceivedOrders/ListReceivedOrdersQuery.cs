using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Orders.Queries.ListReceivedOrders;

/// <summary>
/// Orders against any business the caller owns, each row carrying the
/// business name so a member who owns several businesses can tell them
/// apart.
/// </summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record ListReceivedOrdersQuery : IQuery<IReadOnlyList<OrderResponse>>;

public sealed class ListReceivedOrdersQueryHandler(
    IOrderRepository orders, IBusinessRepository businesses, ICurrentUser currentUser)
    : IRequestHandler<ListReceivedOrdersQuery, Result<IReadOnlyList<OrderResponse>>>
{
    public async Task<Result<IReadOnlyList<OrderResponse>>> Handle(
        ListReceivedOrdersQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<OrderResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var received = await orders.ListReceivedAsync(memberId, cancellationToken);
        var responses = new List<OrderResponse>(received.Count);

        foreach (var order in received)
        {
            var business = await businesses.GetByIdAsync(order.BusinessId, cancellationToken);

            responses.Add(order.ToResponse(business?.Name ?? "A business no longer on the platform"));
        }

        return Result.Success<IReadOnlyList<OrderResponse>>(responses);
    }
}
