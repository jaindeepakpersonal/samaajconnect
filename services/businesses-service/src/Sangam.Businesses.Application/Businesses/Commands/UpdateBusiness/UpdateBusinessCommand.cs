using FluentValidation;
using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Businesses.Commands.CreateBusiness;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;
using Sangam.Businesses.Domain.Businesses;

namespace Sangam.Businesses.Application.Businesses.Commands.UpdateBusiness;

/// <summary>Editing a business the caller owns. Refused as not-found for anyone else's - whether a business exists behind that id is the owner's own business.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record UpdateBusinessCommand(
    Guid BusinessId,
    string Name,
    string CategoryId,
    string? CityId,
    string? LocalityId,
    string? Description,
    string? ContactPhone,
    string? ContactEmail) : ICommand<BusinessResponse>;

public sealed class UpdateBusinessCommandValidator : AbstractValidator<UpdateBusinessCommand>
{
    public UpdateBusinessCommandValidator()
    {
        RuleFor(x => x.BusinessId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).Must(CreateBusinessCommandValidator.BeAGuid)
            .WithMessage("Category is required and must be valid.");
        RuleFor(x => x.CityId).Must(CreateBusinessCommandValidator.BeNullOrAGuid).WithMessage("City is not valid.");
        RuleFor(x => x.LocalityId).Must(CreateBusinessCommandValidator.BeNullOrAGuid).WithMessage("Locality is not valid.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ContactPhone).MaximumLength(20);
        RuleFor(x => x.ContactEmail).MaximumLength(200).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }
}

public sealed class UpdateBusinessCommandHandler(
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateBusinessCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(UpdateBusinessCommand command, CancellationToken cancellationToken)
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

        var fields = new BusinessFields(
            command.Name,
            Guid.Parse(command.CategoryId),
            ParseGuid(command.CityId),
            ParseGuid(command.LocalityId),
            command.Description,
            command.ContactPhone,
            command.ContactEmail);

        business.Update(fields, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }

    private static Guid? ParseGuid(string? value) => value is null ? null : Guid.Parse(value);
}
