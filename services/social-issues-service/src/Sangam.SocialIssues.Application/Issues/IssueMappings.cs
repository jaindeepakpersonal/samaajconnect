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
    ///
    /// The reviewer's moves are offered only to somebody who <i>can act</i> - a
    /// Panch member, or an administrator while no Panch is seated - which is why
    /// an administrator sees an empty list once a Panch exists.
    /// </remarks>
    public static IReadOnlyList<string> AvailableTransitions(
        this SocialIssue issue, Guid viewerId, ReviewAccess access)
    {
        var isAuthor = issue.IsAuthor(viewerId);

        return
        [
            .. Enum.GetValues<IssueStatus>()
                .Where(issue.CanMoveTo)
                .Where(target => SocialIssue.IsAuthorMove(issue.Status, target)
                    ? isAuthor
                    : access.CanAct)
                .Select(target => target.ToString())
        ];
    }

    public static IssueResponse ToResponse(
        this SocialIssue issue, Guid viewerId, ReviewAccess access) => new(
        issue.Id,
        issue.Title,
        issue.Description,
        issue.Category,
        issue.Locality,
        issue.SubmittedByMemberId,
        issue.Status.ToString(),
        issue.IsAuthor(viewerId),
        issue.AvailableTransitions(viewerId, access),
        access.CanAct && issue.Status == IssueStatus.DecisionPending,
        issue.CreatedAt,
        issue.Decision,
        issue.MonitoringGroupId,
        issue.DecidedAt);

    public static IssueDetailResponse ToDetail(
        this SocialIssue issue, Guid viewerId, ReviewAccess access) => new(
        issue.ToResponse(viewerId, access),
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
