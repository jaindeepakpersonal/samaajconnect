namespace Sangam.AuditNotification.Application.Security;

/// <summary>
/// Permission keys this service checks, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Keys are minted by
/// identity-tenant-service and arrive as token claims; this list is only the
/// subset this service gates on.
/// </summary>
public static class PermissionKeys
{
    public const string AuditRead = "Audit.Read";

    /// <summary>
    /// Putting a message in front of every member of a Samaaj at once.
    /// Deliberately not folded into an existing administrative key: it is a
    /// different power from managing members, and a Samaaj should be able to
    /// hand out one without the other.
    /// </summary>
    public const string NotificationsBroadcast = "Notifications.Broadcast";
}
