using FluentValidation;
using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;
using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Application.Biodata.Commands.OptIntoMatrimony;

/// <summary>
/// A member opts into matrimony search, or rejoins after having withdrawn.
/// The starting field values are supplied by the caller, not fetched from
/// member-family-service by this service - member-portal is the party
/// already authenticated to both services and already holds the member's
/// own profile (`GET /v1/members/me`), the same "the client composes what
/// two services separately hold" pattern admin-portal's own Pathshala
/// placement queue already uses for child names.
/// </summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record OptIntoMatrimonyCommand(
    string FullName,
    string Gender,
    // A raw string, like Gender/Diet/FamilyType below - not DateOnly. A
    // DateOnly-typed wire field crashes ASP.NET Core's own JSON model
    // binding with an unhandled 500 the moment the value is missing or an
    // empty string (System.Text.Json's DateOnly converter throws on
    // anything it cannot parse, and an empty string is not JSON `null` -
    // nullability alone does not save it). A member whose base profile has
    // no date of birth set is the ordinary case that sends exactly that, not
    // an edge case. Validated and parsed below instead, the same shape every
    // other enum-like field on this command already uses.
    string? DateOfBirth,
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
    string? PartnerPreferenceNotes,
    bool ConsentGiven,
    string NoticeVersion) : ICommand<MyBiodataResponse>;

public sealed class OptIntoMatrimonyCommandValidator : AbstractValidator<OptIntoMatrimonyCommand>
{
    public OptIntoMatrimonyCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Gender)
            .Must(value => Enum.TryParse<Gender>(value, ignoreCase: true, out _))
            .WithMessage($"Gender must be one of: {string.Join(", ", Enum.GetNames<Gender>())}.");

        RuleFor(x => x.DateOfBirth)
            .Must(value => !string.IsNullOrWhiteSpace(value) && DateOnly.TryParse(value, out _))
            .WithMessage("Date of birth is required.");

        RuleFor(x => x.DateOfBirth)
            .Must(value => DateOnly.Parse(value!) <= DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => DateOnly.TryParse(x.DateOfBirth, out _))
            .WithMessage("Date of birth cannot be in the future.");

        // Refused here, with a readable message, before the aggregate's own
        // unconditional check (defense in depth) ever has a normal request
        // reach it - the same relationship `CreateChildProfileCommandValidator`
        // has with `ChildProfile.Create`'s consent check.
        RuleFor(x => x.DateOfBirth)
            .Must(value => Domain.Biodata.BiodataProfile.AgeInYearsOn(DateOnly.Parse(value!), DateTimeOffset.UtcNow)
                >= Domain.Biodata.BiodataProfile.MinimumAge)
            .When(x => DateOnly.TryParse(x.DateOfBirth, out _))
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

        // Refused here rather than only in the aggregate, so the message
        // reaches the form that asked for it - the same reasoning
        // `CreateChildProfileCommandValidator` already applies to parental
        // consent.
        RuleFor(x => x.ConsentGiven)
            .Equal(true)
            .WithMessage("A biodata profile can only be created with the member's own consent.");

        RuleFor(x => x.NoticeVersion).NotEmpty().WithMessage("Matrimony consent notice version is required.");
    }
}

public sealed class OptIntoMatrimonyCommandHandler(
    IBiodataRepository biodata,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<OptIntoMatrimonyCommand, Result<MyBiodataResponse>>
{
    public async Task<Result<MyBiodataResponse>> Handle(
        OptIntoMatrimonyCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<MyBiodataResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<MyBiodataResponse>(Error.Forbidden(
                "Biodata.NoSamaaj", "Select a Samaaj before opting into matrimony."));
        }

        var fields = ToFields(command);
        var now = clock.UtcNow;

        var existing = await biodata.GetByIdAsync(memberId, cancellationToken);

        if (existing is null)
        {
            var profile = Domain.Biodata.BiodataProfile.OptIn(
                memberId, tenantId, fields, command.ConsentGiven, command.NoticeVersion, now);
            biodata.Add(profile);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(profile.ToOwnerResponse(now));
        }

        existing.Rejoin(fields, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(existing.ToOwnerResponse(now));
    }

    // DateOfBirth is guaranteed present and parseable by the validator,
    // which ValidationBehavior runs before this handler ever sees the
    // command - the same "validated by the time it gets here" guarantee
    // ConsentGiven and NoticeVersion already rely on below.
    private static Domain.Biodata.BiodataFields ToFields(OptIntoMatrimonyCommand command) => new(
        command.FullName,
        Enum.Parse<Gender>(command.Gender, ignoreCase: true),
        DateOnly.Parse(command.DateOfBirth!),
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
}
