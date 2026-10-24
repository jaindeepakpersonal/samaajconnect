using FluentValidation;
using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;
using Sangam.Businesses.Domain.Businesses;

namespace Sangam.Businesses.Application.Businesses.Commands.CreateBusiness;

/// <summary>A member registers a business they own. No moderation - it is visible in the directory the moment this succeeds.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record CreateBusinessCommand(
    string Name,
    // A raw string, not Guid - see UpdateBusinessCommand's own note on why a
    // Guid-typed wire field crashes ASP.NET Core's JSON model binding on
    // malformed or empty input before this validator ever runs.
    string CategoryId,
    string? CityId,
    string? LocalityId,
    string? Description,
    string? ContactPhone,
    string? ContactEmail,
    string? AddressLine = null) : ICommand<BusinessResponse>;

public sealed class CreateBusinessCommandValidator : AbstractValidator<CreateBusinessCommand>
{
    public CreateBusinessCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).Must(BeAGuid).WithMessage("Category is required and must be valid.");
        RuleFor(x => x.CityId).Must(BeNullOrAGuid).WithMessage("City is not valid.");
        RuleFor(x => x.LocalityId).Must(BeNullOrAGuid).WithMessage("Locality is not valid.");
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.AddressLine).MaximumLength(300);
        RuleFor(x => x.ContactPhone).MaximumLength(20);
        RuleFor(x => x.ContactEmail).MaximumLength(200).EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
    }

    internal static bool BeAGuid(string? value) => Guid.TryParse(value, out _);

    internal static bool BeNullOrAGuid(string? value) => value is null || Guid.TryParse(value, out _);
}

public sealed class CreateBusinessCommandHandler(
    IBusinessRepository businesses,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateBusinessCommand, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(CreateBusinessCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<BusinessResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<BusinessResponse>(Error.Forbidden(
                "Business.NoSamaaj", "Select a Samaaj before creating a business."));
        }

        var fields = new BusinessFields(
            command.Name,
            Guid.Parse(command.CategoryId),
            ParseGuid(command.CityId),
            ParseGuid(command.LocalityId),
            command.Description,
            command.ContactPhone,
            command.ContactEmail,
            command.AddressLine);

        var now = clock.UtcNow;
        var business = Business.Create(tenantId, memberId, fields, now);

        businesses.Add(business);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(business.ToResponse(onlyAvailableProducts: false));
    }

    private static Guid? ParseGuid(string? value) => value is null ? null : Guid.Parse(value);
}
