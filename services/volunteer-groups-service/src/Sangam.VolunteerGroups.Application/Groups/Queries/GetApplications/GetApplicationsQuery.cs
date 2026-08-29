using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Groups.Queries.GetApplications;

/// <summary>
/// The president's review queue for one group.
/// </summary>
/// <remarks>
/// The permission is the outer gate and being this group's president is the
/// inner one. An application carries what a member wrote about themselves and
/// who else is waiting - both are the president's to read and nobody else's,
/// which is why this is a separate query rather than a field on the group.
/// </remarks>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record GetApplicationsQuery(Guid GroupId, bool PendingOnly = true)
    : IQuery<IReadOnlyList<GroupApplicationResponse>>;

public sealed class GetApplicationsQueryHandler(IGroupRepository groups, ICurrentUser currentUser)
    : IRequestHandler<GetApplicationsQuery, Result<IReadOnlyList<GroupApplicationResponse>>>
{
    public async Task<Result<IReadOnlyList<GroupApplicationResponse>>> Handle(
        GetApplicationsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<IReadOnlyList<GroupApplicationResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(query.GroupId, cancellationToken);

        if (group is null)
        {
            return Result.Failure<IReadOnlyList<GroupApplicationResponse>>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        if (!group.IsPresident(actorId))
        {
            // "Not found" rather than "forbidden": whether a group has
            // applications waiting is itself the president's business.
            return Result.Failure<IReadOnlyList<GroupApplicationResponse>>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."));
        }

        IReadOnlyList<GroupApplicationResponse> results =
        [
            .. group.Applications
                .Where(a => !query.PendingOnly || a.Status == ApplicationStatus.Pending)
                .OrderBy(a => a.CreatedAt)
                .Select(a => a.ToResponse())
        ];

        return Result.Success(results);
    }
}
