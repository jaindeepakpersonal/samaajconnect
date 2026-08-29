namespace Sangam.Timeline.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>Write a post or a comment. Every member holds it.</summary>
    public const string TimelinePost = "Timeline.Post";

    /// <summary>Approve, reject, hide or restore. Moderators and Samaaj admins.</summary>
    public const string TimelineModerate = "Timeline.Moderate";
}
