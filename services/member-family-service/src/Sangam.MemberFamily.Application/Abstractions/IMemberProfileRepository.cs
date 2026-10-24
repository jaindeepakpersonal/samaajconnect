using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Abstractions;

/// <summary>What the directory can be searched by. Every part is optional.</summary>
public sealed record MemberSearchFilter(
    string? Term = null,
    Guid? LocalityId = null,
    Guid? GautraId = null,
    Guid? ProfessionId = null);

public interface IMemberProfileRepository
{
    /// <summary>Tenant-filtered. Used by everything reachable over HTTP.</summary>
    Task<MemberProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bypasses the tenant filter, for the UserRegistered consumer only.
    /// </summary>
    /// <remarks>
    /// A Kafka consumer has no request and therefore no resolved tenant, so a
    /// filtered existence check would match nothing and every redelivered
    /// registration would create a second profile. The tenant comes from the
    /// event, never from a caller.
    /// </remarks>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The Samaaj directory. Tenant-filtered, so it cannot cross Samaaj.
    /// Matching is on name (substring) and on Locality, Gautra and Profession
    /// (exact master-data ids) only - searching by someone's private mobile
    /// number would leak it one guess at a time.
    /// </summary>
    /// <param name="filter">
    /// Locality is an exact match against the permanent address's Locality -
    /// it is a master-data-service pick, so there is no string to search
    /// toward, only a value to filter on. Gautra carries no privacy level and
    /// filters plainly; Profession is narrowed by <paramref name="viewer"/>.
    /// </param>
    /// <param name="includeUnlisted">
    /// True only for a Samaaj administrator. A member who has taken themselves
    /// out of the directory is still someone an administrator has to be able to
    /// find - correcting a member's details is part of the job, and a member
    /// nobody can look up is a member nobody can help.
    /// </param>
    /// <param name="limit">
    /// The caller (`SearchMembersQueryHandler`) asks for one more row than a
    /// page actually holds, so it can tell whether there is a next page
    /// without a second `COUNT(*)` query.
    /// </param>
    /// <param name="viewer">
    /// Decides what the Profession filter may match. Profession carries a
    /// privacy level, so a filter on it matches only members whose profession
    /// this viewer could already see on the row - their own, anybody's for an
    /// administrator, and otherwise only where it is not Private. Matching a
    /// Private one would confirm it one query at a time.
    /// </param>
    Task<IReadOnlyList<MemberProfile>> SearchAsync(
        MemberSearchFilter filter,
        ProfileViewer viewer,
        int skip,
        int limit,
        bool includeUnlisted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// These profiles, tenant-filtered, listed or not - for reading a known set
    /// of people (a household, a page's parents) rather than searching for
    /// them. Missing ids are simply absent.
    /// </summary>
    Task<IReadOnlyList<MemberProfile>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Bypasses the tenant filter, for consumers. See ExistsAsync.</summary>
    Task<MemberProfile?> GetForConsumerAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many profiles were created in <c>[from, to)</c>, counted in the
    /// database. Tenant-filtered, same as every other reader here.
    /// </summary>
    Task<int> CountCreatedBetweenAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    void Add(MemberProfile profile);
}
