using Microsoft.EntityFrameworkCore;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Domain.Posts;
using Sangam.Timeline.Infrastructure.Persistence;

namespace Sangam.Timeline.Infrastructure.Repositories;

public sealed class PostRepository(TimelineDbContext dbContext) : IPostRepository
{
    public Task<TimelinePost?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Posts
            .Include(p => p.Comments)
            .Include(p => p.Reactions)
            .Include(p => p.ModerationActions)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TimelinePost>> ListFeedAsync(
        int limit, CancellationToken cancellationToken = default) =>
        await dbContext.Posts
            .AsNoTracking()
            .Include(p => p.Reactions)
            .Include(p => p.Comments)
            .Where(p => p.Status == PostStatus.Approved)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimelinePost>> ListForAuthorAsync(
        Guid authorMemberId, int limit, CancellationToken cancellationToken = default) =>
        await dbContext.Posts
            .AsNoTracking()
            .Include(p => p.Reactions)
            .Include(p => p.Comments)
            .Where(p => p.AuthorMemberId == authorMemberId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimelinePost>> ListModerationQueueAsync(
        int limit, CancellationToken cancellationToken = default) =>
        await dbContext.Posts
            .AsNoTracking()
            .Include(p => p.Reactions)
            .Include(p => p.Comments)
            .Include(p => p.ModerationActions)

            // Waiting for review, or already published and complained about.
            // Rejected and hidden posts are not here: a moderator has already
            // decided about those, and a queue that keeps showing settled work
            // stops being a queue.
            .Where(p => p.Status == PostStatus.PendingReview
                || (p.Status == PostStatus.Approved && p.ReportCount > 0))

            // Reported posts first: somebody is waiting on those in a way
            // nobody is waiting on a new post.
            .OrderByDescending(p => p.ReportCount)
            .ThenBy(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(TimelinePost post) => dbContext.Posts.Add(post);

    public async Task<IReadOnlyList<TimelinePost>> ListTouchedByMemberAsync(
        Guid tenantId, Guid memberId, CancellationToken cancellationToken = default) =>
        await dbContext.Posts

            // The erasure consumer runs on no request and so has no resolved
            // tenant; a filtered read here compares every row against
            // Guid.Empty and finds nothing, which would make the erasure a
            // silent no-op. The tenant comes from the event instead. See
            // IPostRepository.
            .IgnoreQueryFilters()
            .Include(p => p.Comments)
            .Where(p => p.TenantId == tenantId
                && (p.AuthorMemberId == memberId
                    || p.Comments.Any(c => c.AuthorMemberId == memberId)))

            // Tracked: the consumer amends these.
            .ToListAsync(cancellationToken);
}
