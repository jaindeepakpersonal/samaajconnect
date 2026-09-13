namespace Sangam.Competition.Application.Security;

/// <summary>
/// Seeded platform roles (DATA-MODEL.md §2). This service gates every
/// command by permission (`PermissionKeys`), never by role list - `SuperAdmin`
/// is referenced only by `TenantAuthorizationBehavior`'s own tenant-override
/// check, the same as every other service's copy of that behavior.
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "SuperAdmin";
}
