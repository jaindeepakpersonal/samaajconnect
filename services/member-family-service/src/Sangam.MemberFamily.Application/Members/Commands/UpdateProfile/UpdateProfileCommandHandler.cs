using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members.Commands.UpdateProfile;

public sealed class UpdateProfileCommandHandler(
    IMemberProfileRepository profiles,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateProfileCommand, Result<MyProfileResponse>>
{
    public async Task<Result<MyProfileResponse>> Handle(
        UpdateProfileCommand command,
        CancellationToken cancellationToken)
    {
        var profile = await profiles.GetByIdAsync(command.MemberId, cancellationToken);

        if (profile is null)
        {
            return Result.Failure<MyProfileResponse>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        // The IDOR guard SECURITY-CHECKLIST.md requires: the write path
        // re-checks the target's tenant rather than trusting that the query
        // filter already did. Skipping this is a blocking review comment.
        if (tenantContext.TenantId is { } tenantId && profile.TenantId != tenantId)
        {
            return Result.Failure<MyProfileResponse>(
                Error.NotFound("Member.NotFound", "No such member in this Samaaj."));
        }

        var isSelf = currentUser.UserId == profile.Id;
        var mayCorrectOthers = currentUser.HasPermission(PermissionKeys.MembersWrite);

        if (!isSelf && !mayCorrectOthers)
        {
            return Result.Failure<MyProfileResponse>(Error.Forbidden(
                "Member.NotYours", "You can only change your own profile."));
        }

        profile.Update(
            command.FullName,
            command.PhotoUrl,
            command.DateOfBirth,
            ParseGender(command.Gender),
            command.Mobile,
            command.Email,
            command.Address,
            command.Locality,
            command.Profession,
            ToFieldPrivacy(command.Privacy),
            clock.UtcNow,
            // Recorded on the event so the audit row can tell a member fixing
            // their own details apart from an admin correcting them.
            currentUser.UserId ?? profile.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(profile.ToOwnerResponse());
    }

    private static Gender ParseGender(string? value) =>
        Enum.TryParse<Gender>(value, ignoreCase: true, out var parsed) ? parsed : Gender.Unspecified;

    private static FieldPrivacy ToFieldPrivacy(PrivacySettings settings) => new(
        Level(settings.Mobile),
        Level(settings.Email),
        Level(settings.Address),
        Level(settings.Profession),
        Level(settings.DateOfBirth));

    /// <summary>
    /// Falls back to Private, never Public. An unreadable privacy value is a
    /// bug, and the safe reading of a bug about visibility is "show less".
    /// </summary>
    private static PrivacyLevel Level(string value) =>
        Enum.TryParse<PrivacyLevel>(value, ignoreCase: true, out var parsed)
            ? parsed
            : PrivacyLevel.Private;
}
