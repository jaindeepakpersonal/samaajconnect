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
    /// Matching is on name and locality only - searching by someone's private
    /// mobile number would leak it one guess at a time.
    /// </summary>
    Task<IReadOnlyList<MemberProfile>> SearchAsync(
        string? term, string? locality, int limit, CancellationToken cancellationToken = default);

    /// <summary>Bypasses the tenant filter, for consumers. See ExistsAsync.</summary>
    Task<MemberProfile?> GetForConsumerAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(MemberProfile profile);
}
