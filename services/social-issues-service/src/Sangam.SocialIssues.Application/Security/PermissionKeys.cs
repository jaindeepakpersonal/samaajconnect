namespace Sangam.SocialIssues.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Review a submitted issue: pick it up, approve, reject, request changes,
    /// publish. Samaaj admins and content moderators hold it.
    /// </summary>
    public const string SocialIssuesApprove = "SocialIssues.Approve";

    /// <summary>
    /// Raise an issue and read the published ones. Every member holds it, via
    /// Members.Read.
    /// </summary>
    public const string MembersRead = "Members.Read";
}
