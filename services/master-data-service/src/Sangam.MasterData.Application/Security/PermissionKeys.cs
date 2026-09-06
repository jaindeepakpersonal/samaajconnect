namespace Sangam.MasterData.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Manage the platform-wide lookups: Country, City, Locality,
    /// ProfessionType, Gender, MaritalStatus. SuperAdmin holds it - this is
    /// reference data every Samaaj shares, not one Samaaj's own list.
    /// </summary>
    public const string MasterDataManage = "MasterData.Manage";

    /// <summary>
    /// Manage one Samaaj's own Gautra list. SamaajAdmin holds it, scoped to
    /// their own tenant the same way every other tenant-scoped write is - see
    /// root CLAUDE.md §6.
    /// </summary>
    public const string GautraManage = "Gautra.Manage";

    /// <summary>Read master data. Every member holds it, via Members.Read.</summary>
    public const string MembersRead = "Members.Read";
}
