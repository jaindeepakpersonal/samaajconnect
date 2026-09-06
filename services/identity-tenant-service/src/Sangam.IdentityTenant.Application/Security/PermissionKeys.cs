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

    /// <summary>
    /// Set a Samaaj's own security-policy overrides.
    /// </summary>
    /// <remarks>
    /// Deliberately its own key rather than <see cref="TenantManage"/>, which
    /// is Super-Admin-only by its own doc comment ("which modules exist is a
    /// platform-level decision") - a Samaaj Admin setting their own Samaaj's
    /// lockout threshold is not that. Not <see cref="AdminUsersManage"/>
    /// either: that is about administrator accounts, not security policy.
    /// </remarks>
    public const string TenantManageSecurityPolicy = "Tenant.ManageSecurityPolicy";
}
