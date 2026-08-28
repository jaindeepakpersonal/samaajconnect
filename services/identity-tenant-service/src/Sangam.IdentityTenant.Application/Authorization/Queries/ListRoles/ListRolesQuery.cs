using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;

/// <summary>
/// The role and permission matrix: every role, and which permissions it carries.
/// </summary>
/// <remarks>
/// <b>Read-only, and that is a decision rather than an omission.</b> The admin
/// wireframe's Role & Permission Matrix screen says "this screen edits it, not
/// just displays it", and it should not, at least not yet.
///
/// Every command and query on this platform declares the roles and permissions
/// it requires as an attribute on the request type. Those declarations are
/// compiled in. A matrix editable at runtime would mean the answer to "who can
/// approve a conversion?" lives half in source control and half in a table
/// somebody changed on a Tuesday, and neither half is reviewable against the
/// other. Worse, the matrix is platform-wide: a Samaaj Admin editing it would
/// be editing what a Samaaj Admin means everywhere.
///
/// Making it editable is a real requirement and a real design problem - it
/// needs per-tenant role definitions, an audit trail of matrix changes, and a
/// floor of permissions no edit may remove or the platform locks itself out.
/// That is its own piece of work. Until then this endpoint tells the truth
/// about what the backend actually enforces, which is more useful than a screen
/// that accepts edits the backend ignores.
///
/// Anyone authenticated may read it. It describes the platform's shape, not any
/// person's access, and a member being able to see why they were refused
/// something is a good thing.
/// </remarks>
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
public sealed record ListRolesQuery : IQuery<RoleMatrixResponse>;

/// <summary>
/// <paramref name="Permissions"/> is the column order the matrix screen renders
/// in, sent once rather than repeated inside every role.
/// </summary>
public sealed record RoleMatrixResponse(
    IReadOnlyList<string> Permissions,
    IReadOnlyList<RoleResponse> Roles,
    bool Editable,
    string EditableNote);

public sealed record RoleResponse(
    Guid Id,
    string Name,
    bool AssignableToAdmins,
    IReadOnlyList<string> Permissions);
