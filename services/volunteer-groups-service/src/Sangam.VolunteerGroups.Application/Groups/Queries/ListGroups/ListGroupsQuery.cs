using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Application.Groups.Queries.ListGroups;

/// <summary>
/// This Samaaj's volunteer groups, from the member-portal wireframe's Groups
/// screen. Each row carries the asking member's own standing with the group,
/// which is what the wireframe's "View / Apply" button needs to know.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListGroupsQuery(string? Status) : IQuery<IReadOnlyList<GroupResponse>>;

public sealed class ListGroupsQueryValidator : AbstractValidator<ListGroupsQuery>
{
    public ListGroupsQueryValidator()
    {
        RuleFor(x => x.Status)
            .Must(s => Enum.TryParse<GroupStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be Active or Inactive.")
            .When(x => !string.IsNullOrWhiteSpace(x.Status));
    }
}

public sealed class ListGroupsQueryHandler(IGroupRepository groups, ICurrentUser currentUser)
    : IRequestHandler<ListGroupsQuery, Result<IReadOnlyList<GroupResponse>>>
{
    public async Task<Result<IReadOnlyList<GroupResponse>>> Handle(
        ListGroupsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<GroupResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        GroupStatus? status = string.IsNullOrWhiteSpace(query.Status)
            ? null
            : Enum.Parse<GroupStatus>(query.Status, ignoreCase: true);

        var found = await groups.ListAsync(status, cancellationToken);

        IReadOnlyList<GroupResponse> results = [.. found.Select(g => g.ToResponse(memberId))];

        return Result.Success(results);
    }
}
