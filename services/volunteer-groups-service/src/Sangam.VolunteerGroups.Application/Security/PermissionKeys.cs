namespace Sangam.VolunteerGroups.Application.Security;

/// <summary>
/// Permission keys this service gates on, in the platform's {Module}.{Action}
/// convention (SECURITY-CHECKLIST.md). Minted by identity-tenant-service and
/// arriving as token claims.
/// </summary>
public static class PermissionKeys
{
    /// <summary>
    /// Create a group, name its president, deactivate it. A Samaaj admin's
    /// decision: a group is part of how a Samaaj organises itself.
    /// </summary>
    public const string VolunteerGroupsManage = "VolunteerGroups.Manage";

    /// <summary>
    /// Run a group you are the president of: decide its applications, read its
    /// queue, assign positions in it.
    /// </summary>
    /// <remarks>
    /// Held by <b>every member</b>, and that is deliberate. A Samaaj admin
    /// creates the group and names its president, so a member cannot make
    /// themselves one - but the president they name is usually an ordinary
    /// member. Gating these on VolunteerGroups.Manage instead would mean a
    /// president could not run their own group unless they were also a Samaaj
    /// admin, which is the bug this split exists to fix.
    ///
    /// The permission is only the outer gate. Being <i>this</i> group's
    /// president is the inner one, checked against the data in each handler -
    /// the same shape as member-family-service's family head, and a stronger
    /// check than a role claim.
    /// </remarks>
    public const string VolunteerGroupsLead = "VolunteerGroups.Lead";

    /// <summary>
    /// Read the Samaaj's groups and apply to one. Every member holds it, via
    /// Members.Read - a volunteer group is part of the member directory in the
    /// sense that matters here: who is in this Samaaj, and what they do in it.
    /// </summary>
    public const string MembersRead = "Members.Read";
}
