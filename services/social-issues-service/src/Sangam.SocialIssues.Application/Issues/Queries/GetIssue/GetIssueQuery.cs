using MediatR;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;

namespace Sangam.SocialIssues.Application.Issues.Queries.GetIssue;

/// <summary>
/// One issue with the record of how it got where it is.
/// </summary>
/// <remarks>
/// The history travels with the detail rather than behind a second call,
/// because it is what answers "why was mine rejected?" — and a screen that has
/// to make an extra request for that will sometimes not make it.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetIssueQuery(Guid IssueId) : IQuery<IssueDetailResponse>;

public sealed class GetIssueQueryHandler(IIssueRepository issues, ICurrentUser currentUser)
    : IRequestHandler<GetIssueQuery, Result<IssueDetailResponse>>
{
    public async Task<Result<IssueDetailResponse>> Handle(
        GetIssueQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IssueDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var canReview = currentUser.HasPermission(PermissionKeys.SocialIssuesApprove);
        var issue = await issues.GetByIdAsync(query.IssueId, cancellationToken);

        if (issue is null)
        {
            return Result.Failure<IssueDetailResponse>(
                Error.NotFound("Issue.NotFound", "No such issue in this Samaaj."));
        }

        // Before publication an issue belongs to its author and its reviewers.
        // Anyone else is told it does not exist rather than that they may not
        // see it — the difference confirms one with that id is there.
        var maySee = issue.IsPublic || issue.IsAuthor(memberId) || canReview;

        return maySee
            ? Result.Success(issue.ToDetail(memberId, canReview))
            : Result.Failure<IssueDetailResponse>(
                Error.NotFound("Issue.NotFound", "No such issue in this Samaaj."));
    }
}
