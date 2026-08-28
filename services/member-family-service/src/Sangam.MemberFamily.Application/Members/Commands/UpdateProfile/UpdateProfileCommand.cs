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
    string? PhotoUrl,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Mobile,
    string? Email,
    string? Address,
    string? Locality,
    string? Profession,
    PrivacySettings Privacy) : ICommand<MyProfileResponse>;

public sealed record PrivacySettings(
    string Mobile,
    string Email,
    string Address,
    string Profession,
    string DateOfBirth);
