namespace Sangam.Timeline.Application.Posts;

/// <summary>
/// A post as the timeline shows it.
/// </summary>
/// <remarks>
/// <paramref name="AuthorMemberId"/> is an id, not a name. Names live in
/// member-family-service, and resolving one here would mean a call per post for
/// a feed - the kind of synchronous reach across a service boundary this repo
/// avoids. The portal already loads the member directory for other screens and
/// can map ids to names client-side.
/// </remarks>
public sealed record PostResponse(
    Guid Id,
    Guid AuthorMemberId,
    string Type,
    string Title,
    string Body,
    string Status,
    int ReportCount,
    IReadOnlyList<ReactionCount> Reactions,

    /// <summary>What the asking member reacted with, if anything.</summary>
    string? MyReaction,
    int CommentCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModeratedAt);

public sealed record ReactionCount(string Type, int Count);

public sealed record CommentResponse(
    Guid Id,
    Guid AuthorMemberId,
    string Body,
    DateTimeOffset CreatedAt);

/// <summary>
/// A post with its comments. Separate from <see cref="PostResponse"/> because
/// a feed of fifty posts should not carry every comment on every one of them.
/// </summary>
public sealed record PostDetailResponse(PostResponse Post, IReadOnlyList<CommentResponse> Comments);

/// <summary>
/// A queue row. Carries the moderation history, because deciding about a post
/// that has been hidden and restored twice needs that context.
/// </summary>
public sealed record ModerationQueueItem(
    PostResponse Post,
    IReadOnlyList<ModerationActionResponse> History,

    /// <summary>
    /// What this moderator may usefully do next, from
    /// <c>TimelinePost.AvailableDecisions</c>. The screen renders these and
    /// derives nothing, so a state added to the domain cannot leave the queue
    /// offering the wrong buttons.
    /// </summary>
    IReadOnlyList<string> AvailableDecisions);

public sealed record ModerationActionResponse(
    Guid ActorUserId,
    string Action,
    string? Reason,
    DateTimeOffset CreatedAt);
