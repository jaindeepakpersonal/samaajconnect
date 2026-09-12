namespace Sangam.Matrimony.Application.Biodata;

/// <summary>
/// A candidate as any same-Samaaj member searching may see them. All or
/// nothing: consenting to matrimony search means every biodata field is
/// visible to any member of the same Samaaj who looks - unlike the base
/// profile's per-field `FieldPrivacy`, there is no partial visibility here,
/// matching how a biodata document is conventionally an all-in-one
/// disclosure once shared. Mobile and email are never part of this; those
/// still flow through the existing, already-privacy-gated member profile.
/// </summary>
public sealed record BiodataResponse(
    Guid MemberId,
    string FullName,
    string? PhotoUrl,
    string Gender,
    int Age,
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
    string? PartnerPreferenceNotes);

/// <summary>
/// The caller's own biodata, plus what the base response never carries:
/// whether it stands, the consent behind it, and the exact date of birth.
/// <see cref="BiodataResponse"/> carries only the derived <c>Age</c> for
/// anyone searching - the owner's own edit form needs the real date back, or
/// `UpdateBiodataCommand`'s required <c>DateOfBirth</c> field would have to
/// be retyped on every edit.
/// </summary>
public sealed record MyBiodataResponse(
    BiodataResponse Biodata,
    DateOnly DateOfBirth,
    bool IsActive,
    DateTimeOffset ConsentGivenAt,
    string ConsentNoticeVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record MatrimonyConsentNoticeResponse(string Version, string Summary, string Attestation);
