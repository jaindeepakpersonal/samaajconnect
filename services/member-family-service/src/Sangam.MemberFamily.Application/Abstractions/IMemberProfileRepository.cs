using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Abstractions;

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
    /// Matching is on name (substring) and locality (exact) only - searching
    /// by someone's private mobile number would leak it one guess at a time.
    /// </summary>
    /// <param name="localityId">
    /// An exact match against the permanent address's Locality, not a
    /// substring - Locality moved from a free-text field to a
    /// master-data-service pick, so there is no longer a string to search
    /// toward, only a value to filter on.
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
    Task<IReadOnlyList<MemberProfile>> SearchAsync(
        string? term,
        Guid? localityId,
        int skip,
        int limit,
        bool includeUnlisted,
        CancellationToken cancellationToken = default);

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
