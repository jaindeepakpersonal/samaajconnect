using MediatR;
using Sangam.Businesses.Api.Extensions;
using Sangam.Businesses.Application.Orders;
using Sangam.Businesses.Application.Orders.Commands.CancelOrder;
using Sangam.Businesses.Application.Orders.Commands.FulfillOrder;
using Sangam.Businesses.Application.Orders.Commands.PlaceOrder;
using Sangam.Businesses.Application.Orders.Commands.RespondToOrder;
using Sangam.Businesses.Application.Orders.Queries.ListMyOrders;
using Sangam.Businesses.Application.Orders.Queries.ListReceivedOrders;

namespace Sangam.Businesses.Api.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/businesses/orders").WithTags("Businesses orders");

        group.MapPost("/", async (
                PlaceOrderRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new PlaceOrderCommand(
                    request.BusinessId,
                    [.. request.Items.Select(i => new PlaceOrderItem(i.ProductId, i.Quantity))],
                    request.Note);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PlaceOrder")
            .WithSummary("Send an order to a business found via the directory.")
            .Produces<OrderResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/mine", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListMyOrdersQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListMyOrders")
            .WithSummary("Orders the caller has placed.")
            .Produces<IReadOnlyList<OrderResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/received", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListReceivedOrdersQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListReceivedOrders")
            .WithSummary("Orders against any business the caller owns.")
            .Produces<IReadOnlyList<OrderResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/{id:guid}/respond", async (
                Guid id, RespondToOrderRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RespondToOrderCommand(id, request.Accept), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RespondToOrder")
            .WithSummary("Accept or decline a pending order. The business owner only.")
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/fulfill", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new FulfillOrderCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("FulfillOrder")
            .WithSummary("Mark an accepted order fulfilled. The business owner only.")
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/cancel", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CancelOrderCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CancelOrder")
            .WithSummary("The buyer cancels a pending order, or the owner cancels an accepted one.")
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record PlaceOrderItemRequest(string ProductId, int Quantity);

    public sealed record PlaceOrderRequest(Guid BusinessId, IReadOnlyList<PlaceOrderItemRequest> Items, string? Note);

    /// <summary>Required, no default - the same reasoning admin-portal's own decision requests already state.</summary>
    public sealed record RespondToOrderRequest(bool Accept);
}
