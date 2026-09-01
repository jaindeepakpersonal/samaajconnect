namespace Sangam.IdentityTenant.Application.Security;

/// <summary>
/// Permission keys owned by this service, in the platform's
/// {Module}.{Action} convention (SECURITY-CHECKLIST.md).
/// </summary>
public static class PermissionKeys
{
    public const string TenantManage = "Tenant.Manage";
    public const string AdminUsersManage = "AdminUsers.Manage";

    /// <summary>
    /// Change what a role may do, for this Samaaj.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from <see cref="AdminUsersManage"/>, which is the
    /// nearest existing key. Inviting an administrator hands somebody an
    /// existing bundle of permissions; this redefines the bundle, for everyone
    /// who holds that role and everyone who ever will. A Samaaj that wants the
    /// first without the second can now withhold it — and a Samaaj Admin
    /// deliberately cannot be stripped of this one, or nobody in that Samaaj
    /// could undo the change.
    /// </remarks>
    public const string RolesManage = "Roles.Manage";
}
