using Sangam.Timeline.Domain.Posts;

namespace Sangam.Timeline.Application.Abstractions;

public interface IPostRepository
{
    /// <summary>Tenant-filtered, with comments and reactions loaded.</summary>
    Task<TimelinePost?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Samaaj's feed: approved posts, newest first.
    /// </summary>
    /// <remarks>
    /// A member's own pending and rejected posts are fetched separately rather
    /// than mixed in here, because "what the Samaaj can see" and "what I wrote"
    /// are different questions and answering them in one query means the feed
    /// query has to know who is asking.
    /// </remarks>
    Task<IReadOnlyList<TimelinePost>> ListFeedAsync(
        int limit, CancellationToken cancellationToken = default);

    /// <summary>This member's own posts, whatever their status.</summary>
    Task<IReadOnlyList<TimelinePost>> ListForAuthorAsync(
        Guid authorMemberId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// What a moderator has to look at: everything awaiting review, plus
    /// anything approved that members have reported.
    /// </summary>
    Task<IReadOnlyList<TimelinePost>> ListModerationQueueAsync(
        int limit, CancellationToken cancellationToken = default);

    void Add(TimelinePost post);
}
