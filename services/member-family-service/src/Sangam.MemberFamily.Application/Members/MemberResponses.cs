namespace Sangam.MemberFamily.Application.Members;

/// <summary>
/// One address on the wire - the free-text line plus the City and Locality
/// ids picked from master-data-service. Shared by <see cref="MemberResponse"/>
/// and <see cref="MyProfileResponse"/>, and reused for both Permanent and
/// Temporary, the same one-shape-used-twice pattern the domain's own
/// <c>ProfileAddress</c> follows.
/// </summary>
public sealed record AddressResponse(string? Line, Guid? CityId, Guid? LocalityId);

/// <summary>
/// A profile as one particular viewer is allowed to see it.
/// </summary>
/// <remarks>
/// Fields the viewer may not see are null rather than omitted or masked. A
/// masked value ("+91 98xxxxxx10") still leaks length and shape, and an
/// omitted key makes every client handle two response shapes.
/// </remarks>
public sealed record MemberResponse(
    Guid Id,
    string FullName,
    string? PhotoUrl,
    Guid? GautraId,
    DateOnly? DateOfBirth,
    string? Mobile,
    string? Email,
    // Never null - City/Locality carry no privacy level (same precedent
    // Locality itself already set) and are always present; only Line is
    // gated, by ToDirectoryResponse, per viewer.
    AddressResponse PermanentAddress,
    // Null when there is none, or when the viewer may not see it - the whole
    // thing is gated together rather than field by field, since it is the
    // supplementary address rather than the one a directory is built around.
    AddressResponse? TemporaryAddress,
    Guid? ProfessionId,
    Guid? QualificationId,
    string Gender);

public sealed record FieldPrivacyResponse(
    string Mobile,
    string Email,
    string Address,
    string Profession,
    string DateOfBirth,
    string Qualification);

/// <summary>The member's own profile, always complete, plus their privacy settings.</summary>
public sealed record MyProfileResponse(
    Guid Id,
    Guid TenantId,
    string FullName,
    string? PhotoUrl,
    DateOnly? DateOfBirth,
    string Gender,
    string? Mobile,
    string? Email,
    Guid? GautraId,
    Guid? ProfessionId,
    Guid? QualificationId,
    AddressResponse PermanentAddress,
    AddressResponse? TemporaryAddress,
    FieldPrivacyResponse Privacy,
    bool IsListedInDirectory,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
