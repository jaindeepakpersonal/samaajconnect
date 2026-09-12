using FluentValidation;
using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;
using Sangam.Businesses.Domain.Orders;

namespace Sangam.Businesses.Application.Orders.Commands.PlaceOrder;

public sealed record PlaceOrderItem(string ProductId, int Quantity);

/// <summary>Send an order to a business found via the directory. Refuses an inactive business, an unavailable or unknown product, or ordering from a business the caller owns.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record PlaceOrderCommand(
    Guid BusinessId,
    IReadOnlyList<PlaceOrderItem> Items,
    string? Note) : ICommand<OrderResponse>;

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    public PlaceOrderCommandValidator()
    {
        RuleFor(x => x.BusinessId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500);

        RuleFor(x => x.Items).NotEmpty().WithMessage("An order needs at least one item.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId).Must(id => Guid.TryParse(id, out _)).WithMessage("Product is not valid.");
            item.RuleFor(i => i.Quantity).GreaterThan(0);
        });

        RuleFor(x => x.Items)
            .Must(items => items.Select(i => i.ProductId).Distinct().Count() == items.Count)
            .WithMessage("The same product cannot appear twice in one order.")
            .When(x => x.Items.Count > 0);
    }
}

public sealed class PlaceOrderCommandHandler(
    IBusinessRepository businesses,
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<PlaceOrderCommand, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } buyerId)
        {
            return Result.Failure<OrderResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var business = await businesses.GetByIdAsync(command.BusinessId, cancellationToken);

        if (business is null || !business.IsActive)
        {
            return Result.Failure<OrderResponse>(Error.NotFound("Business.NotFound", "No such business in this Samaaj."));
        }

        if (tenantContext.TenantId is { } tenantId && business.TenantId != tenantId)
        {
            return Result.Failure<OrderResponse>(Error.NotFound("Business.NotFound", "No such business in this Samaaj."));
        }

        if (buyerId == business.OwnerMemberId)
        {
            return Result.Failure<OrderResponse>(Error.Validation(new Dictionary<string, string[]>
            {
                [nameof(command.BusinessId)] = ["You cannot order from your own business."],
            }));
        }

        var items = new List<OrderItem>(command.Items.Count);

        foreach (var requested in command.Items)
        {
            var product = business.FindProduct(Guid.Parse(requested.ProductId));

            if (product is null || !product.IsAvailable)
            {
                return Result.Failure<OrderResponse>(Error.Conflict(
                    "Product.Unavailable", "One or more products in this order are no longer available."));
            }

            items.Add(new OrderItem(product.Id, product.Name, product.PriceInPaise, requested.Quantity));
        }

        var now = clock.UtcNow;

        var order = Order.Place(business.TenantId, business.Id, business.OwnerMemberId, buyerId, items, command.Note, now);

        orders.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(order.ToResponse(business.Name));
    }
}
