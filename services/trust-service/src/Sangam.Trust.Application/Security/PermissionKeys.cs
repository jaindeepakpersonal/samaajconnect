namespace Sangam.Trust.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Create a Trust, change its status, and add, update or remove members of
    /// its roster. Samaaj admins hold it.
    /// </summary>
    public const string TrustManage = "Trust.Manage";

    /// <summary>Read a Samaaj's Trusts and their rosters. Every member holds it.</summary>
    public const string MembersRead = "Members.Read";
}
