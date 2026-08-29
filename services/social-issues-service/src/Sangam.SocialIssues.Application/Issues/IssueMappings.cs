using Sangam.SocialIssues.Domain.Issues;

namespace Sangam.SocialIssues.Application.Issues;

/// <summary>
/// The one place an issue becomes a response, so there is one place to check
/// that nothing leaks out of it.
/// </summary>
internal static class IssueMappings
{
    /// <summary>
    /// Every status a caller could move this issue to right now.
    /// </summary>
    /// <remarks>
    /// Computed from the same transition table the aggregate enforces, so the
    /// buttons a screen shows and the moves the server accepts cannot drift
    /// apart. A screen that offers Approve on something the server will refuse
    /// is worse than one that offers nothing.
    /// </remarks>
    public static IReadOnlyList<string> AvailableTransitions(
        this SocialIssue issue, Guid viewerId, bool canReview)
    {
        var isAuthor = issue.IsAuthor(viewerId);

        return
        [
            .. Enum.GetValues<IssueStatus>()
                .Where(issue.CanMoveTo)
                .Where(target => SocialIssue.IsAuthorMove(issue.Status, target)
                    ? isAuthor
                    : canReview)
                .Select(target => target.ToString())
        ];
    }

    public static IssueResponse ToResponse(
        this SocialIssue issue, Guid viewerId, bool canReview) => new(
        issue.Id,
        issue.Title,
        issue.Description,
        issue.Category,
        issue.Locality,
        issue.SubmittedByMemberId,
        issue.Status.ToString(),
        issue.IsAuthor(viewerId),
        issue.AvailableTransitions(viewerId, canReview),
        issue.CreatedAt,
        issue.PublishedAt);

    public static IssueDetailResponse ToDetail(
        this SocialIssue issue, Guid viewerId, bool canReview) => new(
        issue.ToResponse(viewerId, canReview),
        [
            .. issue.History
                .OrderBy(h => h.CreatedAt)
                .Select(h => new IssueHistoryResponse(
                    h.FromStatus?.ToString(),
                    h.ToStatus.ToString(),
                    h.ActorUserId,
                    h.Reason,
                    h.CreatedAt))
        ]);
}
