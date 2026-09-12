using MediatR;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;

namespace Sangam.SocialIssues.Application.Issues.Queries.GetApprovalQueue;

/// <summary>
/// What a reviewer has to decide about: submitted, under review, or
/// approved and still waiting to be published. The admin wireframe's
/// approval queue - one queue for every stage a reviewer's own next move
/// applies to, not only the first decision.
/// </summary>
/// <remarks>
/// Oldest first. A member waiting on an answer about their own community has
/// been waiting longest, and a queue ordered any other way needs explaining to
/// the people it passes over.
/// </remarks>
[RequiresPermission(PermissionKeys.SocialIssuesApprove)]
public sealed record GetApprovalQueueQuery : IQuery<IReadOnlyList<IssueResponse>>;

public sealed class GetApprovalQueueQueryHandler(
    IIssueRepository issues, ICurrentUser currentUser)
    : IRequestHandler<GetApprovalQueueQuery, Result<IReadOnlyList<IssueResponse>>>
{
    public async Task<Result<IReadOnlyList<IssueResponse>>> Handle(
        GetApprovalQueueQuery query,
        CancellationToken cancellationToken)
    {
        var waiting = await issues.ListAwaitingDecisionAsync(cancellationToken);
        var viewerId = currentUser.UserId ?? Guid.Empty;

        IReadOnlyList<IssueResponse> results =
            [.. waiting.Select(i => i.ToResponse(viewerId, canReview: true))];

        return Result.Success(results);
    }
}
