namespace Sangam.Boli.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Set an occasion up, define its Boli types, open a Boli for bidding, close
    /// it, and record who won. Samaaj admins and Boli managers hold it.
    /// </summary>
    public const string BoliManage = "Boli.Manage";

    /// <summary>
    /// Announce a recorded result to the Samaaj.
    /// </summary>
    /// <remarks>
    /// A separate key from <see cref="BoliManage"/>, which the platform's
    /// authorization catalogue already anticipated. Recording a result is a note
    /// about what happened in the room and can be corrected before anybody sees
    /// it; publishing is irreversible through this API and is what the Samaaj is
    /// then owed. Both are currently granted to the same two roles, so the split
    /// separates nothing yet — but it is the right thing to gate on, and a Samaaj
    /// that wants a second pair of eyes on announcements can grant one without
    /// the other without this service changing.
    /// </remarks>
    public const string BoliPublishResults = "Boli.PublishResults";

    /// <summary>Read a Boli and bid on it. Every member holds it.</summary>
    public const string MembersRead = "Members.Read";
}
