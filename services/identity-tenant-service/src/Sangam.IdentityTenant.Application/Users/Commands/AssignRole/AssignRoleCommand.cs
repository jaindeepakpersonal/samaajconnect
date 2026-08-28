using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.AssignRole;

/// <summary>
/// Grants or revokes one role for one account in the caller's Samaaj.
/// </summary>
/// <remarks>
/// One command for both directions rather than two, because the screen is a set
/// of checkboxes and the only difference between them is a boolean. Splitting
/// them would duplicate every one of the checks below, which are the whole
/// substance of this command.
///
/// The scope is always the caller's own Samaaj. It is not a parameter: a
/// tenant id supplied by the caller is exactly what SECURITY-CHECKLIST.md
/// forbids, and a Super Admin who needs to act on another Samaaj uses the
/// gateway override, which lands in the same `ITenantContext` and is logged.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.AdminUsersManage)]
public sealed record AssignRoleCommand(Guid UserId, string Role, bool Granted)
    : ICommand<AssignRoleResponse>;

public sealed record AssignRoleResponse(
    Guid UserId,
    string Role,
    bool Granted,
    bool Changed,
    IReadOnlyCollection<string> Roles);
