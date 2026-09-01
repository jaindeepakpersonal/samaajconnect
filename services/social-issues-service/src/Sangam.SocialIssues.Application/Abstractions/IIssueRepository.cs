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

    /// <summary>
    /// Every issue this member submitted, or acted on in its history, in the
    /// given Samaaj.
    /// </summary>
    /// <remarks>
    /// For the erasure consumer, and the reason it takes an explicit tenant:
    /// a consumer resolves no tenant, so the global query filter would compare
    /// against <c>Guid.Empty</c> and match nothing. That failure is silent — an
    /// erasure that quietly erases nothing — and it is the same one
    /// pathshala-service hit on <c>ListForChildAsync</c>. The implementation
    /// ignores the filter and applies the tenant from the event by hand.
    ///
    /// History entries are included because a submitter is an actor in their
    /// own workflow, so a reason they wrote can sit on an issue that is not
    /// theirs.
    /// </remarks>
    Task<IReadOnlyList<SocialIssue>> ListTouchedByMemberAsync(
        Guid tenantId, Guid memberId, CancellationToken cancellationToken = default);
}
