using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members.Commands.CorrectMemberDetails;

/// <summary>
/// A Samaaj administrator correcting somebody else's factual details.
/// </summary>
/// <remarks>
/// <para>
/// <b>The write existed and could not be performed.</b> `Members.Write` is
/// granted to SamaajAdmin, `SERVICES.md` says an administrator holding it may
/// correct anyone's profile in their Samaaj, and
/// <c>UpdateProfileCommand</c> accepted them. But that command replaces the
/// profile whole and therefore requires <c>privacy</c> and
/// <c>isListedInDirectory</c> - and **no read an administrator can make returns
/// either one**. <c>ToDirectoryResponse</c>, the only mapper an administrator
/// reaches for somebody else, omits both; <c>ToOwnerResponse</c>, which carries
/// them, is only ever built for the caller's own profile.
/// </para>
/// <para>
/// So an administrator correcting a misspelt name had two outcomes available,
/// both silent and both wrong. Send levels they guessed, and they overwrite
/// choices the member made. Send anything unparseable - an empty object, an
/// omitted field - and <c>UpdateProfileCommandHandler.Level</c> falls back to
/// <c>Private</c>, hiding every field the member had chosen to share. The
/// member is not told either way.
/// </para>
/// <para>
/// This command is the answer, and the shape of it is the point: it carries no
/// privacy fields at all, so there is nothing to guess and nothing to send by
/// accident. Correcting a phone number is administrative work; deciding who may
/// see the phone number is the member's, and the two were only ever one call
/// because there was only ever one command. It is the same line the child photo
/// endpoints draw, and the same one that keeps deciding a join request with the
/// household head rather than with an administrator.
/// </para>
/// <para>
/// <b>Self is refused here rather than allowed.</b> An administrator editing
/// their own profile has <c>PATCH /v1/members/{id}</c> and their own screen,
/// where they can see and set their privacy - so routing themselves through the
/// correction path would be the one caller who loses the ability to change
/// something they are entitled to change, without being told why.
/// </para>
/// </remarks>
[RequiresRoles(Roles.SamaajAdmin, Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.MembersWrite)]
public sealed record CorrectMemberDetailsCommand(
    Guid MemberId,
    string FullName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Mobile,
    string? Email,
    string? Address,
    string? Locality,
    string? Profession) : ICommand<MemberResponse>;

public sealed class CorrectMemberDetailsCommandValidator
    : AbstractValidator<CorrectMemberDetailsCommand>
{
    public CorrectMemberDetailsCommandValidator()
    {
        // The same lengths as UpdateProfileCommandValidator, because they are
        // the same columns. There is deliberately no privacy rule to keep in
        // step, which is one fewer pair of lists that can drift.
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Mobile).MaximumLength(20);
        RuleFor(x => x.Email).MaximumLength(320);
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Locality).MaximumLength(120);
        RuleFor(x => x.Profession).MaximumLength(120);

        RuleFor(x => x.Gender)
            .Must(value => Enum.TryParse<Gender>(value, ignoreCase: true, out _))
            .WithMessage($"Gender must be one of: {string.Join(", ", Enum.GetNames<Gender>())}.")
            .When(x => !string.IsNullOrWhiteSpace(x.Gender));

        RuleFor(x => x.DateOfBirth)
            .Must(value => value!.Value <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Date of birth cannot be in the future.")
            .When(x => x.DateOfBirth.HasValue);
    }
}

public sealed class CorrectMemberDetailsCommandHandler(
    IMemberProfileRepository profiles,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CorrectMemberDetailsCommand, Result<MemberResponse>>
{
    public async Task<Result<MemberResponse>> Handle(
        CorrectMemberDetailsCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<MemberResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var profile = await profiles.GetByIdAsync(command.MemberId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<MemberResponse>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        // The IDOR guard root CLAUDE.md §6 requires: the write path re-checks
        // the target's tenant rather than trusting the query filter alone.
        if (tenantContext.TenantId is { } tenantId && profile.TenantId != tenantId)
        {
            return Result.Failure<MemberResponse>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        if (profile.Id == actorId)
        {
            return Result.Failure<MemberResponse>(Error.Conflict(
                "Member.CorrectYourOwnProfileInstead",
                "This is your own profile. Change it from your profile screen, where you "
                + "can also set what your Samaaj may see."));
        }

        profile.CorrectDetails(
            command.FullName,
            command.DateOfBirth,
            ParseGender(command.Gender),
            command.Mobile,
            command.Email,
            command.Address,
            command.Locality,
            command.Profession,
            clock.UtcNow,
            actorId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The directory response, through the same mapper the read used - and
        // for an administrator that is the full record, because IsVisibleTo
        // lets a Samaaj admin past every level. Returning ToOwnerResponse would
        // hand back privacy settings this command deliberately cannot set, and
        // a screen shown a field it cannot write is a screen that will
        // eventually try.
        var viewer = new ProfileViewer(actorId, IsSamaajAdmin: true);

        return Result.Success(profile.ToDirectoryResponse(viewer));
    }

    private static Gender ParseGender(string? value) =>
        Enum.TryParse<Gender>(value, ignoreCase: true, out var parsed) ? parsed : Gender.Unspecified;
}
