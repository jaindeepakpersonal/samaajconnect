using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.ChangeTenantStatus;

/// <summary>
/// Activates, deactivates or archives a Samaaj. Separate from creation because
/// letting a Samaaj serve traffic is its own decision with its own audit trail.
/// </summary>
/// <remarks>
/// <paramref name="Password"/> is the Super Admin's own, and is required when
/// the target status takes the Samaaj out of service. Erasing a single account
/// already re-asks for a password; deactivating a whole Samaaj signs out every
/// one of its members and is at least as consequential, and archiving is the
/// only status change on the platform that cannot be undone.
///
/// The requirement is decided by the <i>target</i> status alone, not by whether
/// the change would actually do anything. Making it depend on current state
/// would mean the same call sometimes needs a password and sometimes does not,
/// which is worse to implement against and worse to reason about than asking
/// once more than strictly necessary.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin)]
[RequiresPermission(PermissionKeys.TenantManage)]
public sealed record ChangeTenantStatusCommand(Guid TenantId, string Status, string? Password = null)
    : ICommand<TenantResponse>
{
    /// <summary>
    /// Whether <paramref name="status"/> takes a Samaaj out of service, and so
    /// needs the password.
    /// </summary>
    /// <remarks>
    /// Activating does not: it restores service, and it is reversible by the
    /// very call that undid it. The asymmetry is deliberate - a step-up on the
    /// harmless direction only teaches people to type their password without
    /// reading the screen.
    /// </remarks>
    public static bool RequiresStepUp(TenantStatus status) =>
        status is TenantStatus.Inactive or TenantStatus.Archived;
}
