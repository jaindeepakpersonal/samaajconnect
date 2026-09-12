using FluentValidation;
using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;
using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Application.Biodata.Commands.UpdateBiodata;

/// <summary>Editing an already-opted-in biodata profile. Carries no consent fields - opting in already happened.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record UpdateBiodataCommand(
    string FullName,
    string Gender,
    DateOnly DateOfBirth,
    Guid? GautraId,
    Guid? ProfessionId,
    Guid? QualificationId,
    Guid? CityId,
    Guid? LocalityId,
    int? HeightCm,
    Guid? MaritalStatusId,
    string? Diet,
    string? FamilyType,
    string? FatherOccupation,
    string? MotherOccupation,
    int? SiblingsCount,
    bool? Manglik,
    string? HoroscopeNotes,
    string? AboutMe,
    int? PreferredAgeMin,
    int? PreferredAgeMax,
    string? PartnerPreferenceNotes) : ICommand<MyBiodataResponse>;

public sealed class UpdateBiodataCommandValidator : AbstractValidator<UpdateBiodataCommand>
{
    public UpdateBiodataCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Gender)
            .Must(value => Enum.TryParse<Gender>(value, ignoreCase: true, out _))
            .WithMessage($"Gender must be one of: {string.Join(", ", Enum.GetNames<Gender>())}.");

        RuleFor(x => x.DateOfBirth)
            .Must(value => value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.");

        RuleFor(x => x.DateOfBirth)
            .Must(value => Domain.Biodata.BiodataProfile.AgeInYearsOn(value, DateTimeOffset.UtcNow)
                >= Domain.Biodata.BiodataProfile.MinimumAge)
            .WithMessage($"A biodata profile requires the member to be at least "
                + $"{Domain.Biodata.BiodataProfile.MinimumAge}.");

        RuleFor(x => x.Diet)
            .Must(value => value is null || Enum.TryParse<Domain.Biodata.Diet>(value, ignoreCase: true, out _))
            .WithMessage($"Diet must be one of: {string.Join(", ", Enum.GetNames<Domain.Biodata.Diet>())}.");

        RuleFor(x => x.FamilyType)
            .Must(value => value is null || Enum.TryParse<Domain.Biodata.FamilyType>(value, ignoreCase: true, out _))
            .WithMessage($"Family type must be one of: {string.Join(", ", Enum.GetNames<Domain.Biodata.FamilyType>())}.");

        RuleFor(x => x.FatherOccupation).MaximumLength(120);
        RuleFor(x => x.MotherOccupation).MaximumLength(120);
        RuleFor(x => x.HoroscopeNotes).MaximumLength(500);
        RuleFor(x => x.AboutMe).MaximumLength(1000);
        RuleFor(x => x.PartnerPreferenceNotes).MaximumLength(500);
    }
}

public sealed class UpdateBiodataCommandHandler(
    IBiodataRepository biodata,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateBiodataCommand, Result<MyBiodataResponse>>
{
    public async Task<Result<MyBiodataResponse>> Handle(UpdateBiodataCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<MyBiodataResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var profile = await biodata.GetByIdAsync(memberId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<MyBiodataResponse>(
                Error.NotFound("Biodata.NotFound", "You have not opted into matrimony yet."));
        }

        if (tenantContext.TenantId is { } tenantId && profile.TenantId != tenantId)
        {
            return Result.Failure<MyBiodataResponse>(
                Error.NotFound("Biodata.NotFound", "You have not opted into matrimony yet."));
        }

        var now = clock.UtcNow;

        var fields = new Domain.Biodata.BiodataFields(
            command.FullName,
            Enum.Parse<Gender>(command.Gender, ignoreCase: true),
            command.DateOfBirth,
            command.GautraId,
            command.ProfessionId,
            command.QualificationId,
            command.CityId,
            command.LocalityId,
            command.HeightCm,
            command.MaritalStatusId,
            command.Diet is null ? null : Enum.Parse<Domain.Biodata.Diet>(command.Diet, ignoreCase: true),
            command.FamilyType is null ? null : Enum.Parse<Domain.Biodata.FamilyType>(command.FamilyType, ignoreCase: true),
            command.FatherOccupation,
            command.MotherOccupation,
            command.SiblingsCount,
            command.Manglik,
            command.HoroscopeNotes,
            command.AboutMe,
            command.PreferredAgeMin,
            command.PreferredAgeMax,
            command.PartnerPreferenceNotes);

        profile.Update(fields, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(profile.ToOwnerResponse(now));
    }
}
