using Sangam.SocialIssues.Domain.Common;

namespace Sangam.SocialIssues.Domain.Issues;

/// <summary>
/// A member raised an issue.
/// </summary>
/// <remarks>
/// Carries the category, which is a fixed vocabulary, and not the title,
/// description or locality. What a member says is wrong in their community can
/// name neighbours, describe a dispute, or be the thing a reviewer decides not
/// to publish - and audit-notification-service records payloads verbatim into
/// an append-only table. Putting it there would publish, permanently, the one
/// thing the review step exists to hold back.
/// </remarks>
public sealed record IssueSubmittedDomainEvent(
    Guid IssueId,
    Guid TenantId,
    Guid SubmittedByMemberId,
    string Category,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "social-issues.issue.submitted.v1";
}

/// <summary>
/// The issue moved. Carries both statuses, which is the before-state
/// SECURITY-CHECKLIST.md asks for, and both people: the author who is waiting
/// on the answer and the reviewer who gave it.
/// </summary>
/// <remarks>
/// No reason text. A reviewer's note explaining a rejection is written to the
/// member it is about, and belongs in the issue's own history rather than in a
/// log nobody can redact.
/// </remarks>
public sealed record IssueStatusChangedDomainEvent(
    Guid IssueId,
    Guid TenantId,
    Guid SubmittedByMemberId,
    Guid ActorUserId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "social-issues.issue.status-changed.v1";
}

/// <summary>
/// The issue is now visible to the Samaaj. Separate from the status change so a
/// consumer that only cares about publication does not have to filter every
/// move in an eight-state workflow.
/// </summary>
public sealed record IssuePublishedDomainEvent(
    Guid IssueId,
    Guid TenantId,
    string Category,
    string? Locality,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "social-issues.issue.published.v1";
}
