namespace Sangam.Timeline.Domain.Posts;

/// <summary>
/// A comment on an approved post. Owned by <see cref="TimelinePost"/>, which is
/// why it has no independent factory: a comment cannot exist without the post
/// having accepted it.
/// </summary>
public sealed class PostComment
{
    public Guid Id { get; private set; }
    public Guid PostId { get; private set; }
    public Guid AuthorMemberId { get; private set; }
    public string Body { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    private PostComment() { }   // EF Core

    internal PostComment(Guid postId, Guid authorMemberId, string body, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        PostId = postId;
        AuthorMemberId = authorMemberId;
        Body = body;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Replaces the text with a placeholder. Returns false when it already
    /// reads that way, so a redelivered erasure changes nothing.
    /// </summary>
    /// <remarks>
    /// Internal: only <see cref="TimelinePost.ErasePersonalDataOf"/> calls it,
    /// so the reasoning about what erasure does here lives in one place.
    /// </remarks>
    internal bool EraseBody(string placeholder)
    {
        if (Body == placeholder)
        {
            return false;
        }

        Body = placeholder;

        return true;
    }
}

/// <summary>One member's reaction to a post. At most one per member per post.</summary>
public sealed class PostReaction
{
    public Guid Id { get; private set; }
    public Guid PostId { get; private set; }
    public Guid MemberId { get; private set; }
    public ReactionType Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PostReaction() { }   // EF Core

    internal PostReaction(Guid postId, Guid memberId, ReactionType type, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        PostId = postId;
        MemberId = memberId;
        Type = type;
        CreatedAt = createdAt;
    }
}

/// <summary>
/// One moderator decision, kept forever.
/// </summary>
/// <remarks>
/// Append-only within the aggregate: there is no way to change or remove one.
/// A post that was hidden and later restored has both facts on the record, and
/// which moderator did each. "Why is this not on the timeline?" is a question a
/// member will ask, and it needs an answer that does not depend on somebody
/// remembering.
/// </remarks>
public sealed class ModerationAction
{
    public Guid Id { get; private set; }
    public Guid PostId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public ModerationDecision Action { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ModerationAction() { }   // EF Core

    internal ModerationAction(
        Guid postId,
        Guid actorUserId,
        ModerationDecision action,
        string? reason,
        DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        PostId = postId;
        ActorUserId = actorUserId;
        Action = action;
        Reason = reason;
        CreatedAt = createdAt;
    }
}
