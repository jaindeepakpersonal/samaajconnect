using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Queries.GetCurrentUser;

/// <summary>
/// The caller's own account, roles and permissions. Every authenticated role
/// may ask about themselves, so this is annotated with the full role list
/// rather than left unannotated - an unannotated request is denied.
/// </summary>
[RequiresRoles(
    Roles.SuperAdmin,
    Roles.SamaajAdmin,
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.BoliManager)]
public sealed record GetCurrentUserQuery : IQuery<CurrentUserResponse>;
