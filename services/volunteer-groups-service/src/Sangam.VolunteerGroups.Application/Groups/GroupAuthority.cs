using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Groups;

/// <summary>
/// Who may run a group. Ordinarily only its president - but a group created
/// without one would otherwise have nobody able to accept a joiner, so nobody
/// to nominate, vote or be elected, and the only way out would be an admin
/// choosing a president blind.
/// </summary>
/// <remarks>
/// So, and only while a group has <b>no</b> president, a Samaaj admin (or Super
/// Admin) may decide its join requests. Removing members keeps waiting for a
/// president. The moment one exists that power stops, which is why this reads
/// the group's current state each time rather than granting anybody a
/// standing role. Roles and elections are different - see
/// <see cref="CanRunElections"/>.
///
/// An Area Admin holds the same admin powers, for groups in their own
/// locality only (<see cref="VolunteerGroup.LocalityId"/>); a group serving no
/// particular locality is the Samaaj Admin's alone.
/// </remarks>
internal static class GroupAuthority
{
    /// <summary>Whether this caller administers this particular group.</summary>
    public static bool Administers(this ICurrentUser user, IAreaScope area, VolunteerGroup group) =>
        user.IsInRole(Roles.SamaajAdmin)
        || user.IsInRole(Roles.SuperAdmin)
        || area.AdministersLocality(group.LocalityId);

    /// <summary>The president, or an admin while there is no president.</summary>
    public static bool CanLead(this VolunteerGroup group, Guid actorId, bool actorIsGroupAdmin) =>
        group.IsPresident(actorId) || (group.HasNoPresident && actorIsGroupAdmin);

    /// <summary>
    /// Who defines the group's volunteer roles, assigns them, and runs the
    /// elections for them: its president, and its administrators at any time
    /// (changed 2026-09-26 - before, an admin could run only the President
    /// election, and only while there was no president). Unlike
    /// <see cref="CanLead"/>, an admin's power here does not wait for the
    /// presidency to be empty: which roles a group has and who fills them is
    /// how the Samaaj organises it, and a president-only rule left every other
    /// role in a president-less group with no way to be filled.
    /// </summary>
    public static bool CanRunElections(this VolunteerGroup group, Guid actorId, bool actorIsGroupAdmin) =>
        group.IsPresident(actorId) || actorIsGroupAdmin;
}
