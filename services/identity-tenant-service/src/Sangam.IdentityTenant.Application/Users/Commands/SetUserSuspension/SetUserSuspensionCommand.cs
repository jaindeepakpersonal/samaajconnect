using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.SetUserSuspension;

/// <summary>
/// Suspends or reinstates an account in the caller's Samaaj.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every piece of this was already built and reachable by nothing.</b>
/// `UserStatus.Suspended` exists; `LoginCommandHandler` refuses a suspended
/// account; `SessionService.ContinueAsync` re-reads status on every refresh and
/// force-revokes the whole chain the moment it finds anything but `Active` -
/// which is what makes suspending someone take effect within one access
/// token's lifetime rather than at their next sign-in, exactly as
/// `SECURITY-CHECKLIST.md` and this service's own `CLAUDE.md` already claimed.
/// What none of that had was a way in: `User.Suspend()` was called from
/// nowhere but a unit test that sets up a scenario, and `Reinstate()` from
/// nowhere at all. A Samaaj administrator had no way to act on a problem
/// account short of the platform operator archiving the whole Samaaj.
/// </para>
/// <para>
/// One command for both directions, the same reasoning
/// `AssignRoleCommand` gives for its own `Granted` boolean: the screen is a
/// toggle, and splitting the two would duplicate every check below for no
/// benefit.
/// </para>
/// <para>
/// <paramref name="Password"/> is the caller's own, required only to suspend -
/// exactly the asymmetry `ChangeTenantStatusCommand` draws between taking
/// something out of service and restoring it. Reinstating is reversible by the
/// very call that undid it; a step-up on that direction only teaches people to
/// type a password without reading the screen.
/// </para>
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.AdminUsersManage)]
public sealed record SetUserSuspensionCommand(Guid UserId, bool Suspended, string? Password = null)
    : ICommand<UserStatusResponse>
{
    public static bool RequiresStepUp(bool suspended) => suspended;
}

public sealed record UserStatusResponse(Guid UserId, string Status, bool Changed);
