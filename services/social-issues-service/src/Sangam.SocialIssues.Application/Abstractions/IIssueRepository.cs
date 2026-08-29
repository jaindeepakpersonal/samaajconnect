using Sangam.SocialIssues.Domain.Issues;

namespace Sangam.SocialIssues.Application.Abstractions;

public interface IIssueRepository
{
    /// <summary>Tenant-filtered, with the status history loaded.</summary>
    Task<SocialIssue?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues the Samaaj can see: published and closed ones, newest first.
    /// </summary>
    Task<IReadOnlyList<SocialIssue>> ListPublicAsync(
        string? category, CancellationToken cancellationToken = default);

    /// <summary>
    /// This member's own issues, whatever their status - the wireframe's "My
    /// Submissions" card, which shows a progress strip for each.
    /// </summary>
    Task<IReadOnlyList<SocialIssue>> ListForAuthorAsync(
        Guid authorMemberId, CancellationToken cancellationToken = default);

    /// <summary>Waiting for a reviewer: submitted, and under review.</summary>
    Task<IReadOnlyList<SocialIssue>> ListAwaitingDecisionAsync(
        CancellationToken cancellationToken = default);

    void Add(SocialIssue issue);
}
