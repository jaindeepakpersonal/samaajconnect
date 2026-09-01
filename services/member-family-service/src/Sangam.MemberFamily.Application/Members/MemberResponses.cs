namespace Sangam.MemberFamily.Application.Members;

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
    string? Locality,
    DateOnly? DateOfBirth,
    string? Mobile,
    string? Email,
    string? Address,
    string? Profession,
    string Gender);

public sealed record FieldPrivacyResponse(
    string Mobile,
    string Email,
    string Address,
    string Profession,
    string DateOfBirth);

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
    string? Address,
    string? Locality,
    string? Profession,
    FieldPrivacyResponse Privacy,
    bool IsListedInDirectory,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
