using Sangam.MemberFamily.Domain.Children;

namespace Sangam.MemberFamily.Application.Abstractions;

public interface IChildRepository
{
    /// <summary>Tenant-filtered. Used by everything reachable over HTTP.</summary>
    Task<ChildProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bypasses the tenant filter, for the conversion-completion consumer only.
    /// </summary>
    /// <remarks>
    /// A Kafka consumer has no request and therefore no resolved tenant, so a
    /// filtered lookup compares against Guid.Empty and finds nothing - which
    /// reads as "this child was deleted" and silently drops the event. The
    /// tenant is on the event; it is never supplied by a caller.
    /// </remarks>
    Task<ChildProfile?> GetForConsumerAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChildProfile>> ListForFamilyAsync(
        Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>Every child in this Samaaj, for the admin's eligibility list.</summary>
    Task<IReadOnlyList<ChildProfile>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Bypasses the tenant filter, for consumers.</summary>
    Task<IReadOnlyList<ChildProfile>> ListForConsumerAsync(
        Guid familyId, CancellationToken cancellationToken = default);

    void Add(ChildProfile child);
}

public interface IChildConversionRepository
{
    Task<ChildConversionRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>An undecided request for this child, if one is outstanding.</summary>
    Task<ChildConversionRequest?> GetPendingForChildAsync(
        Guid childProfileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChildConversionRequest>> ListPendingAsync(
        CancellationToken cancellationToken = default);

    void Add(ChildConversionRequest request);
}
