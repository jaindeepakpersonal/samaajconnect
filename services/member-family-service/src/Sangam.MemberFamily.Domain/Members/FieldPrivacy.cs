namespace Sangam.MemberFamily.Domain.Members;

/// <summary>
/// Per-field visibility for one profile.
/// </summary>
/// <remarks>
/// Per field, not one switch for the whole profile: SECURITY-CHECKLIST.md is
/// explicit that the directory must respect PrivacyLevel "per profile field,
/// not just an all-or-nothing visibility toggle". A member who is happy to be
/// listed by name is not thereby happy to publish their address.
/// </remarks>
public sealed record FieldPrivacy(
    PrivacyLevel Mobile,
    PrivacyLevel Email,
    PrivacyLevel Address,
    PrivacyLevel Profession,
    PrivacyLevel DateOfBirth)
{
    /// <summary>
    /// What a new member gets. Contact details start closed and the member
    /// opens them, rather than starting open and hoping they notice.
    /// </summary>
    public static FieldPrivacy Default { get; } = new(
        Mobile: PrivacyLevel.SamaajOnly,
        Email: PrivacyLevel.Private,
        Address: PrivacyLevel.Private,
        Profession: PrivacyLevel.SamaajOnly,
        DateOfBirth: PrivacyLevel.SamaajOnly);
}
