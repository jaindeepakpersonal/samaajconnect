using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Elections;

/// <summary>
/// Which of a group's roles an election may be run for, shared by opening one
/// and editing one so an edit cannot reach a state opening could not.
/// </summary>
internal static class ElectionRoles
{
    /// <summary>
    /// The role, when it belongs to this group, is not Volunteer, and has no
    /// other election still running for it.
    /// </summary>
    /// <remarks>
    /// <paramref name="roleId"/> is already a parseable Guid - both validators
    /// check it. Volunteer is refused because it has no seats to fill: it is
    /// what everybody holds until elected to something else. A second running
    /// election for one role would let two ballots decide the same seats.
    /// </remarks>
    public static async Task<Result<GroupRole>> ResolveAsync(
        VolunteerGroup group,
        string roleId,
        IElectionRepository elections,
        Guid? exceptElectionId,
        CancellationToken cancellationToken)
    {
        var role = group.FindRole(Guid.Parse(roleId));

        if (role is null)
        {
            return Result.Failure<GroupRole>(
                Error.NotFound("Role.NotFound", "This group has no such role."));
        }

        if (!role.IsElected)
        {
            return Result.Failure<GroupRole>(Error.Conflict(
                "Role.NotElected",
                "Everybody in the group is a Volunteer until elected to another role, so it has no election."));
        }

        if (await elections.HasUnpublishedForRoleAsync(group.Id, role.Id, exceptElectionId, cancellationToken))
        {
            return Result.Failure<GroupRole>(Error.Conflict(
                "Election.AlreadyRunning",
                $"An election for {role.Title} is already running. Publish it before starting another."));
        }

        return Result.Success(role);
    }
}
