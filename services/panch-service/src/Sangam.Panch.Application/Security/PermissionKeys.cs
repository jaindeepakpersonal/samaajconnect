namespace Sangam.Panch.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Set an election up, open and close its windows, approve nominations,
    /// publish the result, and remove a panel member. Samaaj admins hold it.
    /// </summary>
    public const string PanchManage = "Panch.Manage";

    /// <summary>Nominate, vote, and read the panel. Every member holds it, via Members.Read.</summary>
    public const string MembersRead = "Members.Read";
}
