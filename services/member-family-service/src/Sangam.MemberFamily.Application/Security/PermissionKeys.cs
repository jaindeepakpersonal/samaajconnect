namespace Sangam.MemberFamily.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    public const string MembersRead = "Members.Read";
    public const string MembersWrite = "Members.Write";
    public const string FamilyWrite = "Family.Write";
    public const string FamilyApproveConversion = "Family.ApproveConversion";
}
