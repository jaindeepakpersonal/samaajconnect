namespace Sangam.Competition.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Register, withdraw, cast a score as an assigned judge, and read the
    /// directory. Every member holds it, the same broadly-granted key
    /// businesses-service's and volunteer-groups-service's own read-side
    /// queries already reuse rather than minting one of their own.
    /// </summary>
    public const string MembersRead = "Members.Read";

    /// <summary>
    /// Set a competition up, move it, assign judges, manage rounds and
    /// entries, and publish results. Samaaj-admin-only, the same shape
    /// `CelebrityVoting.Configure` already has - an organiser permission,
    /// not a new platform role.
    /// </summary>
    public const string CompetitionManage = "Competition.Manage";
}
