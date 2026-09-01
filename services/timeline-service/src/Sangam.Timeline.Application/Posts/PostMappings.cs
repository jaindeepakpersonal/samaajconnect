using Sangam.Timeline.Domain.Posts;

namespace Sangam.Timeline.Application.Posts;

/// <summary>
/// The single place a post becomes a response, so there is one place to check
/// that nothing leaks out of it.
/// </summary>
internal static class PostMappings
{
    public static PostResponse ToResponse(this TimelinePost post, Guid? viewerId) => new(
        post.Id,
        post.AuthorMemberId,
        post.Type.ToString(),
        post.Title,
        post.Body,
        post.Status.ToString(),

        // The count is shown to whoever can see the post; a moderator is the
        // only one who sees a post *because* of it. Who reported it is never
        // returned at all - see TimelinePost.ReportCount.
        post.ReportCount,
        [.. post.Reactions
            .GroupBy(r => r.Type)
            .Select(g => new ReactionCount(g.Key.ToString(), g.Count()))
            .OrderBy(r => r.Type, StringComparer.Ordinal)],
        viewerId is { } id
            ? post.Reactions.FirstOrDefault(r => r.MemberId == id)?.Type.ToString()
            : null,
        post.Comments.Count,
        post.CreatedAt,
        post.ModeratedAt);

    public static PostDetailResponse ToDetail(this TimelinePost post, Guid? viewerId) => new(
        post.ToResponse(viewerId),
        [.. post.Comments
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentResponse(c.Id, c.AuthorMemberId, c.Body, c.CreatedAt))]);

    public static ModerationQueueItem ToQueueItem(this TimelinePost post) => new(
        post.ToResponse(viewerId: null),
        [.. post.ModerationActions
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new ModerationActionResponse(
                a.ActorUserId, a.Action.ToString(), a.Reason, a.CreatedAt))],
        [.. post.AvailableDecisions.Select(d => d.ToString())]);
}
