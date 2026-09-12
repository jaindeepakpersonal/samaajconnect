using FluentValidation;
using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Businesses.Commands.UpdateProduct;

[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record UpdateProductCommand(
    Guid BusinessId,
    Guid ProductId,
    string Name,
    string? Description,
    int PriceInPaise) : ICommand<BusinessResponse>;

public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.BusinessId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.PriceInPaise).GreaterThan(0);
    }
}

public sealed class UpdateProductCommandHandler(
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateProductCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<BusinessResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var business = await businesses.GetByIdAsync(command.BusinessId, cancellationToken);

        if (business is null || business.OwnerMemberId != memberId)
        {
            return Result.Failure<BusinessResponse>(Error.NotFound("Business.NotFound", "No such business."));
        }

        if (tenantContext.TenantId is { } tenantId && business.TenantId != tenantId)
        {
            return Result.Failure<BusinessResponse>(Error.NotFound("Business.NotFound", "No such business."));
        }

        var now = clock.UtcNow;

        if (!business.UpdateProduct(command.ProductId, command.Name, command.Description, command.PriceInPaise, now))
        {
            return Result.Failure<BusinessResponse>(Error.NotFound("Product.NotFound", "No such product on this business."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }
}
