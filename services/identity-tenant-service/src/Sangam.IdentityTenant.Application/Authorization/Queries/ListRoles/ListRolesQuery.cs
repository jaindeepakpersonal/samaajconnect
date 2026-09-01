using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;

/// <summary>
/// The role and permission matrix: every role, and which permissions it carries.
/// </summary>
/// <remarks>
/// <b>Editable per Samaaj</b>, which it was not until the three things this
/// remark used to name as preconditions existed: per-tenant role definitions,
/// an audit trail of matrix changes, and a floor of permissions no edit may
/// remove. See <c>SetRolePermissionCommand</c> and <c>MatrixEditing</c>.
///
/// The objection that used to stand here - that an editable matrix would put
/// "who can approve a conversion?" half in source control and half in a table -
/// does not apply to the shape it was eventually given. A command's
/// <c>[RequiresPermission]</c> is still compiled in and still says what that
/// command needs; the matrix says who carries a permission. Those are the two
/// halves of role-based access control and they answer different questions.
///
/// Anyone authenticated may read it. It describes the platform's shape, not any
/// person's access, and a member being able to see why they were refused
/// something is a good thing. <c>Editable</c> on the response says whether
/// <i>this caller</i> may change it, which is not the same question.
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
    IReadOnlyList<string> Permissions,

    // False for SuperAdmin, which is platform administration rather than
    // Samaaj administration. Sent per role so the screen disables that row
    // rather than discovering the refusal on submit.
    bool Editable);
