using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Groups.Queries.ListMemberGroups;

/// <summary>
/// The volunteer groups one member belongs to, and the role they hold in each -
/// the "Volunteer groups" card on their profile page.
/// </summary>
/// <remarks>
/// Nothing here is news to anyone who may ask: every group's roster, roles
/// included, is open to any member of the Samaaj on the group's own page
/// (<c>GetGroupQuery</c>, the same <c>Members.Read</c>). This only answers the
/// same question from the member's side instead of the group's. It never says
/// what a member has applied to - a pending application is between them and
/// the president.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListMemberGroupsQuery(Guid MemberId) : IQuery<IReadOnlyList<MemberGroupResponse>>;

public sealed class ListMemberGroupsQueryValidator : AbstractValidator<ListMemberGroupsQuery>
{
    public ListMemberGroupsQueryValidator() => RuleFor(x => x.MemberId).NotEmpty();
}

public sealed class ListMemberGroupsQueryHandler(IGroupRepository groups)
    : IRequestHandler<ListMemberGroupsQuery, Result<IReadOnlyList<MemberGroupResponse>>>
{
    public async Task<Result<IReadOnlyList<MemberGroupResponse>>> Handle(
        ListMemberGroupsQuery query,
        CancellationToken cancellationToken)
    {
        var found = await groups.ListForMemberAsync(query.MemberId, cancellationToken);

        IReadOnlyList<MemberGroupResponse> results =
        [
            .. found.Select(group =>
            {
                var membership = group.Members.First(m => m.MemberId == query.MemberId);

                return new MemberGroupResponse(
                    group.Id,
                    group.Name,
                    group.FocusArea,
                    group.Status.ToString(),
                    group.PhotoId is null ? null : $"/v1/volunteer-groups/groups/{group.Id}/photo",
                    group.FindRole(membership.RoleId)?.Title,
                    group.IsPresident(query.MemberId),
                    membership.JoinedAt);
            }),
        ];

        return Result.Success(results);
    }
}

/// <summary>One group a member belongs to, from their side.</summary>
public sealed record MemberGroupResponse(
    Guid GroupId,
    string Name,
    string? FocusArea,
    string Status,
    string? PhotoUrl,
    string? RoleTitle,
    bool IsPresident,
    DateTimeOffset JoinedAt);
