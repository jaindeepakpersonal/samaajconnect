using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Members.Commands.UpdateProfile;

/// <summary>
/// Updates a profile. A member may update their own; a Samaaj admin holding
/// Members.Write may correct anyone's in their Samaaj (SERVICES.md).
/// </summary>
/// <remarks>
/// The role list here is the coarse gate. Whether *this* caller may edit *this*
/// profile is decided in the handler, because it depends on the target - which
/// is exactly the check SECURITY-CHECKLIST.md calls the IDOR guard.
/// </remarks>
[RequiresRoles(
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.BoliManager,
    Roles.SamaajAdmin,
    Roles.SuperAdmin)]
public sealed record UpdateProfileCommand(
    Guid MemberId,
    string FullName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Mobile,
    string? Email,
    string? Address,
    string? Locality,
    string? Profession,
    PrivacySettings Privacy,
    // Nullable and required, for the same reason Privacy is: this command
    // replaces the whole profile, so a body that omits this is malformed rather
    // than partial. Defaulting it to true would quietly put a member who had
    // taken themselves out of the directory back into it, which is the exact
    // failure the privacy rule below it exists to prevent.
    bool? IsListedInDirectory) : ICommand<MyProfileResponse>;

public sealed record PrivacySettings(
    string Mobile,
    string Email,
    string Address,
    string Profession,
    string DateOfBirth);
