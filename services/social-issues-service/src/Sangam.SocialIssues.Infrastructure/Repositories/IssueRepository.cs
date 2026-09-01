using Microsoft.EntityFrameworkCore;
using Sangam.SocialIssues.Application.Abstractions;
using Sangam.SocialIssues.Domain.Issues;
using Sangam.SocialIssues.Infrastructure.Persistence;

namespace Sangam.SocialIssues.Infrastructure.Repositories;

public sealed class IssueRepository(SocialIssuesDbContext dbContext) : IIssueRepository
{
    /// <summary>
    /// Statuses the Samaaj at large can see. Kept here rather than expressed as
    /// <c>IsPublic</c> in the query, because a computed property cannot be
    /// translated to SQL and the failure mode is a silent client-side
    /// evaluation that loads every issue in the Samaaj.
    /// </summary>
    private static readonly IssueStatus[] PublicStatuses =
        [IssueStatus.Published, IssueStatus.Closed];

    private static readonly IssueStatus[] AwaitingDecision =
        [IssueStatus.Submitted, IssueStatus.UnderReview];

    public Task<SocialIssue?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Issues
            .Include(i => i.History)
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SocialIssue>> ListPublicAsync(
        string? category, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Issues
            .AsNoTracking()
            .Where(i => PublicStatuses.Contains(i.Status));

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim();

            query = query.Where(i => i.Category == wanted);
        }

        return await query.OrderByDescending(i => i.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SocialIssue>> ListForAuthorAsync(
        Guid authorMemberId, CancellationToken cancellationToken = default) =>
        await dbContext.Issues
            .AsNoTracking()
            .Where(i => i.SubmittedByMemberId == authorMemberId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SocialIssue>> ListAwaitingDecisionAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Issues
            .AsNoTracking()
            .Where(i => AwaitingDecision.Contains(i.Status))

            // Oldest first: whoever has been waiting longest for an answer
            // about their own community gets it first.
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    public void Add(SocialIssue issue) => dbContext.Issues.Add(issue);

    public async Task<IReadOnlyList<SocialIssue>> ListTouchedByMemberAsync(
        Guid tenantId, Guid memberId, CancellationToken cancellationToken = default) =>
        await dbContext.Issues

            // The erasure consumer runs on no request and so has no resolved
            // tenant; a filtered read here compares every row against
            // Guid.Empty and finds nothing, which would make the erasure a
            // silent no-op. The tenant comes from the event instead. See
            // IIssueRepository.
            .IgnoreQueryFilters()
            .Include(i => i.History)
            .Where(i => i.TenantId == tenantId
                && (i.SubmittedByMemberId == memberId
                    || i.History.Any(h => h.ActorUserId == memberId)))

            // Tracked: the consumer amends these.
            .ToListAsync(cancellationToken);
}
