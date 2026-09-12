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
    public static MemberResponse ToDirectoryResponse(this MemberProfile profile, ProfileViewer viewer)
    {
        var addressVisible = profile.IsVisibleTo(profile.Privacy.Address, viewer);

        return new(
            profile.Id,
            // Name, photo, Gautra and a permanent address's City/Locality are
            // what a directory is for; they carry no per-field level and are
            // visible to the Samaaj - a Samaaj sub-lineage reads as an identity
            // marker here, the same family as a name, not as a phone number.
            profile.FullName,
            PhotoPath(profile.Id, profile.PhotoImageId),
            profile.GautraId,
            profile.IsVisibleTo(profile.Privacy.DateOfBirth, viewer) ? profile.DateOfBirth : null,
            profile.IsVisibleTo(profile.Privacy.Mobile, viewer) ? profile.Mobile : null,
            profile.IsVisibleTo(profile.Privacy.Email, viewer) ? profile.Email : null,
            new AddressResponse(
                addressVisible ? profile.PermanentAddress.Line : null,
                profile.PermanentAddress.CityId,
                profile.PermanentAddress.LocalityId),
            // The temporary address is the supplementary one, so it is gated
            // as a whole rather than City/Locality staying visible the way
            // the permanent address's do - there is no directory-listing
            // reason for it to be visible at all once Line is hidden.
            addressVisible && profile.TemporaryAddress is { } temporary
                ? new AddressResponse(temporary.Line, temporary.CityId, temporary.LocalityId)
                : null,
            profile.IsVisibleTo(profile.Privacy.Profession, viewer) ? profile.ProfessionId : null,
            profile.IsVisibleTo(profile.Privacy.Qualification, viewer) ? profile.QualificationId : null,
            profile.Gender.ToString());
    }

    public static MyProfileResponse ToOwnerResponse(this MemberProfile profile) => new(
        profile.Id,
        profile.TenantId,
        profile.FullName,
        PhotoPath(profile.Id, profile.PhotoImageId),
        profile.DateOfBirth,
        profile.Gender.ToString(),
        profile.Mobile,
        profile.Email,
        profile.GautraId,
        profile.ProfessionId,
        profile.QualificationId,
        new AddressResponse(
            profile.PermanentAddress.Line, profile.PermanentAddress.CityId, profile.PermanentAddress.LocalityId),
        profile.TemporaryAddress is { } temporary
            ? new AddressResponse(temporary.Line, temporary.CityId, temporary.LocalityId)
            : null,
        new FieldPrivacyResponse(
            profile.Privacy.Mobile.ToString(),
            profile.Privacy.Email.ToString(),
            profile.Privacy.Address.ToString(),
            profile.Privacy.Profession.ToString(),
            profile.Privacy.DateOfBirth.ToString(),
            profile.Privacy.Qualification.ToString()),
        profile.IsListedInDirectory,
        profile.CreatedAt,
        profile.UpdatedAt);
}
