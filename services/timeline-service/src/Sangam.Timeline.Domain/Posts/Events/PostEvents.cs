using Sangam.Timeline.Domain.Common;

namespace Sangam.Timeline.Domain.Posts;

/// <summary>
/// A post was written. <paramref name="Status"/> says whether it is waiting for
/// a moderator or already on the timeline.
/// </summary>
/// <remarks>
/// Carries no title or body. audit-notification-service records payloads
/// verbatim into an append-only table, and the whole point of moderation is
/// that some of what members write should not end up on the Samaaj's timeline -
/// putting it somewhere deliberately hard to redact would defeat that on the
/// one post that most needed it.
/// </remarks>
public sealed record PostSubmittedDomainEvent(
    Guid PostId,
    Guid TenantId,
    Guid AuthorMemberId,
    string Type,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "timeline.post.submitted.v1";
}

/// <summary>
/// A moderator decided about a post. Carries the previous status as well as the
/// new one, which is the before-state SECURITY-CHECKLIST.md asks for on a status
/// change - and no reason text, because a moderator's note about a member is
/// about that member.
/// </summary>
public sealed record PostModeratedDomainEvent(
    Guid PostId,
    Guid TenantId,
    Guid AuthorMemberId,
    Guid ActorUserId,
    string Decision,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "timeline.post.moderated.v1";
}

/// <summary>
/// A member reported a post. Names the post and the running count, never the
/// reporter: in a community organisation where everyone knows each other, a
/// reporter who could be identified is a reporter who stays quiet.
/// </summary>
public sealed record PostReportedDomainEvent(
    Guid PostId,
    Guid TenantId,
    int ReportCount,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "timeline.post.reported.v1";
}
