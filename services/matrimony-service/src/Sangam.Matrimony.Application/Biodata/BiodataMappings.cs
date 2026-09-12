using Sangam.Matrimony.Domain.Biodata;

namespace Sangam.Matrimony.Application.Biodata;

public static class BiodataMappings
{
    /// <summary>
    /// Where a client fetches this member's photo. Always the path, never
    /// conditional on whether one exists - unlike member-family-service's own
    /// `PhotoPath`, this service never learns whether a photo was uploaded,
    /// only the member's id. A viewer with no photo gets the same 404 the
    /// member photo endpoint already answers with, and the screen handles
    /// that the same way every other photo display on this platform does.
    /// </summary>
    private static string PhotoPath(Guid memberId) => $"/v1/members/{memberId}/photo";

    public static BiodataResponse ToResponse(this BiodataProfile profile, DateTimeOffset now) => new(
        profile.Id,
        profile.FullName,
        PhotoPath(profile.Id),
        profile.Gender.ToString(),
        BiodataProfile.AgeInYearsOn(profile.DateOfBirth, now),
        profile.GautraId,
        profile.ProfessionId,
        profile.QualificationId,
        profile.CityId,
        profile.LocalityId,
        profile.HeightCm,
        profile.MaritalStatusId,
        profile.Diet?.ToString(),
        profile.FamilyType?.ToString(),
        profile.FatherOccupation,
        profile.MotherOccupation,
        profile.SiblingsCount,
        profile.Manglik,
        profile.HoroscopeNotes,
        profile.AboutMe,
        profile.PreferredAgeMin,
        profile.PreferredAgeMax,
        profile.PartnerPreferenceNotes);

    public static MyBiodataResponse ToOwnerResponse(this BiodataProfile profile, DateTimeOffset now) => new(
        profile.ToResponse(now),
        profile.DateOfBirth,
        profile.IsActive,
        profile.Consent.GivenAt,
        profile.Consent.NoticeVersion,
        profile.CreatedAt,
        profile.UpdatedAt);
}
