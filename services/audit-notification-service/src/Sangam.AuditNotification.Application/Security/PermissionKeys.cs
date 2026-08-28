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
}
