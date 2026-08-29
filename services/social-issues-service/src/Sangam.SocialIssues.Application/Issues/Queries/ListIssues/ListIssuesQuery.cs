using MediatR;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;

namespace Sangam.SocialIssues.Application.Issues.Queries.ListIssues;

/// <summary>
/// The published issues, plus the asking member's own whatever their status —
/// the wireframe's "Published Issues" and "My Submissions" cards in one call.
/// </summary>
/// <remarks>
/// Their own drafts and rejections are visible to them and to nobody else. A
/// member who submits something and then cannot see it anywhere reasonably
/// concludes it was lost.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListIssuesQuery(string? Category) : IQuery<IReadOnlyList<IssueResponse>>;

public sealed class ListIssuesQueryHandler(IIssueRepository issues, ICurrentUser currentUser)
    : IRequestHandler<ListIssuesQuery, Result<IReadOnlyList<IssueResponse>>>
{
    public async Task<Result<IReadOnlyList<IssueResponse>>> Handle(
        ListIssuesQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<IssueResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var canReview = currentUser.HasPermission(PermissionKeys.SocialIssuesApprove);

        var published = await issues.ListPublicAsync(query.Category, cancellationToken);
        var mine = await issues.ListForAuthorAsync(memberId, cancellationToken);

        // Two queries rather than one that knows who is asking. Merged here
        // because a published issue this member raised comes back from both.
        IReadOnlyList<IssueResponse> results =
        [
            .. published
                .Concat(mine)
                .DistinctBy(i => i.Id)
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => i.ToResponse(memberId, canReview))
        ];

        return Result.Success(results);
    }
}
