namespace Sangam.CelebrityVoting.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Set a campaign up, open and close its windows, approve nominations and
    /// publish the result. Samaaj admins hold it.
    /// </summary>
    public const string CelebrityVotingConfigure = "CelebrityVoting.Configure";

    /// <summary>Nominate and vote. Every member holds it, via Members.Read.</summary>
    public const string MembersRead = "Members.Read";
}
