using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members;

internal static class MemberMappings
{
    /// <summary>
    /// Applies the per-field privacy rules. This is the only place a profile
    /// becomes a directory response, so there is one place to check that the
    /// rules are actually applied.
    /// </summary>
    public static MemberResponse ToDirectoryResponse(this MemberProfile profile, ProfileViewer viewer) =>
        new(
            profile.Id,
            // Name, photo and locality are what a directory is for; they carry
            // no per-field level and are visible to the Samaaj.
            profile.FullName,
            profile.PhotoUrl,
            profile.Locality,
            profile.IsVisibleTo(profile.Privacy.DateOfBirth, viewer) ? profile.DateOfBirth : null,
            profile.IsVisibleTo(profile.Privacy.Mobile, viewer) ? profile.Mobile : null,
            profile.IsVisibleTo(profile.Privacy.Email, viewer) ? profile.Email : null,
            profile.IsVisibleTo(profile.Privacy.Address, viewer) ? profile.Address : null,
            profile.IsVisibleTo(profile.Privacy.Profession, viewer) ? profile.Profession : null,
            profile.Gender.ToString());

    public static MyProfileResponse ToOwnerResponse(this MemberProfile profile) => new(
        profile.Id,
        profile.TenantId,
        profile.FullName,
        profile.PhotoUrl,
        profile.DateOfBirth,
        profile.Gender.ToString(),
        profile.Mobile,
        profile.Email,
        profile.Address,
        profile.Locality,
        profile.Profession,
        new FieldPrivacyResponse(
            profile.Privacy.Mobile.ToString(),
            profile.Privacy.Email.ToString(),
            profile.Privacy.Address.ToString(),
            profile.Privacy.Profession.ToString(),
            profile.Privacy.DateOfBirth.ToString()),
        profile.CreatedAt,
        profile.UpdatedAt);
}
