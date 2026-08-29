using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Queries.GetGroup;

/// <summary>
/// One group with its members, from the wireframe's group-detail screen.
/// Applications are not here: only the president may read those.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetGroupQuery(Guid GroupId) : IQuery<GroupDetailResponse>;

public sealed class GetGroupQueryHandler(IGroupRepository groups, ICurrentUser currentUser)
    : IRequestHandler<GetGroupQuery, Result<GroupDetailResponse>>
{
    public async Task<Result<GroupDetailResponse>> Handle(
        GetGroupQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<GroupDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var group = await groups.GetByIdAsync(query.GroupId, cancellationToken);

        return group is null
            ? Result.Failure<GroupDetailResponse>(
                Error.NotFound("Group.NotFound", "No such group in this Samaaj."))
            : Result.Success(group.ToDetail(memberId));
    }
}
