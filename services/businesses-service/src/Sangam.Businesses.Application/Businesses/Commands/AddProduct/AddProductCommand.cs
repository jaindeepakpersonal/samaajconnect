using FluentValidation;
using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Businesses.Commands.AddProduct;

[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record AddProductCommand(
    Guid BusinessId,
    string Name,
    string? Description,
    int PriceInPaise) : ICommand<BusinessResponse>;

public sealed class AddProductCommandValidator : AbstractValidator<AddProductCommand>
{
    public AddProductCommandValidator()
    {
        RuleFor(x => x.BusinessId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.PriceInPaise).GreaterThan(0);
    }
}

public sealed class AddProductCommandHandler(
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<AddProductCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(AddProductCommand command, CancellationToken cancellationToken)
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

        business.AddProduct(command.Name, command.Description, command.PriceInPaise, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }
}
