namespace Sangam.IdentityTenant.Application.Security;

/// <summary>
/// Permission keys owned by this service, in the platform's
/// {Module}.{Action} convention (SECURITY-CHECKLIST.md).
/// </summary>
public static class PermissionKeys
{
    public const string TenantManage = "Tenant.Manage";
    public const string AdminUsersManage = "AdminUsers.Manage";
}
