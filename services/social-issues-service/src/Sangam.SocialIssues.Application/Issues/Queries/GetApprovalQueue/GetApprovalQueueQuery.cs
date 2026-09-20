using MediatR;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;

namespace Sangam.SocialIssues.Application.Issues.Queries.GetApprovalQueue;

/// <summary>
/// What the Panch has to look at: submitted, under review, or in front of it
/// awaiting a decision. One queue for every stage a reviewer's own next move
/// applies to.
/// </summary>
/// <remarks>
/// Oldest first. A member waiting on an answer about their own community has
/// been waiting longest, and a queue ordered any other way needs explaining to
/// the people it passes over.
///
/// <b>The outer permission is <c>Members.Read</c>, not
/// <c>SocialIssues.Approve</c>.</b> A Panch member is an ordinary member and
/// holds no reviewer permission; gating on it would lock out the people this
/// queue is now for. Who may read it is decided in the handler, against the
/// roster - a seated Panch member, or a holder of the reviewer permission
/// (which lets an administrator see the queue, though not act on it, once a
/// Panch exists).
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetApprovalQueueQuery : IQuery<IReadOnlyList<IssueResponse>>;

public sealed class GetApprovalQueueQueryHandler(
    IIssueRepository issues, ICurrentUser currentUser, ReviewAccessResolver reviewAccess)
    : IRequestHandler<GetApprovalQueueQuery, Result<IReadOnlyList<IssueResponse>>>
{
    public async Task<Result<IReadOnlyList<IssueResponse>>> Handle(
        GetApprovalQueueQuery query,
        CancellationToken cancellationToken)
    {
        var access = await reviewAccess.ResolveAsync(cancellationToken);

        if (!access.CanSeeQueue)
        {
            return Result.Failure<IReadOnlyList<IssueResponse>>(Error.Forbidden(
                "Issue.NotAReviewer",
                "Only the Samaaj Panch reviews issues."));
        }

        var waiting = await issues.ListAwaitingDecisionAsync(cancellationToken);
        var viewerId = currentUser.UserId ?? Guid.Empty;

        IReadOnlyList<IssueResponse> results =
            [.. waiting.Select(i => i.ToResponse(viewerId, access))];

        return Result.Success(results);
    }
}
