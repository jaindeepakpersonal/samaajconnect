using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Members.Queries.GetMyProfile;

/// <summary>The caller's own profile, always complete regardless of privacy settings.</summary>
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
public sealed record GetMyProfileQuery : IQuery<MyProfileResponse>;
