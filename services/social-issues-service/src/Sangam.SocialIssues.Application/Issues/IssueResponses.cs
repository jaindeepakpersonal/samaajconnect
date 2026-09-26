namespace Sangam.SocialIssues.Application.Issues;

/// <summary>
/// An issue as the member's list and the reviewer's queue show it.
/// </summary>
/// <remarks>
/// <paramref name="SubmittedByMemberId"/> is an id. The reviewer's queue card
/// in the wireframe says "Submitted by Member 1088", which is what an id looks
/// like when the portal has not resolved it - and names live in
/// member-family-service.
/// </remarks>
public sealed record IssueResponse(
    Guid Id,
    string Title,
    string Description,
    string Category,
    string? Locality,

    /// <summary>The master-data-service locality it concerns, or null.</summary>
    Guid? LocalityId,
    Guid SubmittedByMemberId,
    string Status,

    /// <summary>True when the asking member raised this one.</summary>
    bool IsMine,

    /// <summary>
    /// What this caller may do to it next, given the workflow and their
    /// permission. The wireframe's queue shows Approve / Reject / Request
    /// Changes, and the member's card shows a progress strip - both need to
    /// know which moves are live rather than guessing from the status.
    /// </summary>
    IReadOnlyList<string> AvailableTransitions,

    /// <summary>
    /// True when this caller may record the Panch's decision on this issue now:
    /// it is <c>DecisionPending</c> and they sit on the Panch (or, while no Panch
    /// is seated, hold the reviewer permission).
    /// </summary>
    bool CanDecide,
    DateTimeOffset CreatedAt,

    /// <summary>What the Panch decided. Null until it has.</summary>
    string? Decision,

    /// <summary>
    /// The volunteer group the Panch handed monitoring to, if it did. An id -
    /// names live in volunteer-groups-service and the portal resolves them.
    /// </summary>
    Guid? MonitoringGroupId,
    DateTimeOffset? DecidedAt);

/// <summary>One step in the issue's life, for the history view.</summary>
public sealed record IssueHistoryResponse(
    string? FromStatus,
    string ToStatus,
    Guid ActorUserId,
    string? Reason,
    DateTimeOffset CreatedAt);

/// <summary>
/// An issue with the record of how it got here. The history is what answers
/// "why was mine rejected?", so it travels with the detail rather than behind a
/// second call the portal might not make.
/// </summary>
public sealed record IssueDetailResponse(
    IssueResponse Issue,
    IReadOnlyList<IssueHistoryResponse> History);
