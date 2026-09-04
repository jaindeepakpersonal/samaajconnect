using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members;

public static class MemberMappings
{
    /// <summary>
    /// Where a client fetches this photo, or null when there is none.
    /// </summary>
    /// <remarks>
    /// The wire field is still called <c>photoUrl</c> and still holds a string a
    /// client puts in an <c>img src</c>, so nothing in either portal had to
    /// change when the platform started hosting the bytes. What changed is who
    /// it points at: it used to be whatever host the member typed, and is now
    /// this platform, on a path that authorizes every request.
    ///
    /// Relative on purpose. Both apps are same-origin with the gateway - the
    /// member portal because the gateway serves it at the root, the admin panel
    /// because nginx proxies /v1 - so a relative path works from both without
    /// this service having to know either origin. Building an absolute URL would
    /// mean configuring a public hostname into a service that has never needed
    /// one.
    /// </remarks>
    private static string? PhotoPath(Guid memberId, Guid? imageId) =>
        imageId is null ? null : $"/v1/members/{memberId}/photo";

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
            PhotoPath(profile.Id, profile.PhotoImageId),
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
        PhotoPath(profile.Id, profile.PhotoImageId),
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
        profile.IsListedInDirectory,
        profile.CreatedAt,
        profile.UpdatedAt);
}
