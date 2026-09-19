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
/// Admin) may decide its join requests and run its <i>President</i> election.
/// Everything else - assigning positions, removing members, any other
/// election - keeps waiting for a president. The moment one exists these
/// powers stop, which is why this reads the group's current state each time
/// rather than granting anybody a standing role.
/// </remarks>
internal static class GroupAuthority
{
    public static bool IsGroupAdmin(this ICurrentUser user) =>
        user.IsInRole(Roles.SamaajAdmin) || user.IsInRole(Roles.SuperAdmin);

    /// <summary>The president, or an admin while there is no president.</summary>
    public static bool CanLead(this VolunteerGroup group, Guid actorId, bool actorIsGroupAdmin) =>
        group.IsPresident(actorId) || (group.HasNoPresident && actorIsGroupAdmin);

    /// <summary>
    /// As <see cref="CanLead"/>, but for an election: an admin may run only the
    /// election for the president's own position.
    /// </summary>
    public static bool CanRunElection(
        this VolunteerGroup group, Guid actorId, bool actorIsGroupAdmin, string positionTitle) =>
        group.IsPresident(actorId)
        || (group.HasNoPresident
            && actorIsGroupAdmin
            && string.Equals(positionTitle.Trim(), VolunteerGroup.PresidentPosition, StringComparison.OrdinalIgnoreCase));
}
