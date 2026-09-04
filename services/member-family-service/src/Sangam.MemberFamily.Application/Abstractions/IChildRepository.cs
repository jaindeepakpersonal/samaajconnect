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

    /// <summary>
    /// The named children, tenant-filtered, for resolving ids another service
    /// holds. An id belonging to a different Samaaj comes back missing rather
    /// than refused.
    /// </summary>
    Task<IReadOnlyList<ChildProfile>> ListByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Bypasses the tenant filter, for consumers.</summary>
    Task<IReadOnlyList<ChildProfile>> ListForConsumerAsync(
        Guid familyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every child record held on one member's parental consent, wherever it
    /// sits. Bypasses the tenant filter, for the erasure consumer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Erasure used to find children by the household the erasing member
    /// <i>headed</i>, which was the same set for as long as nobody could leave
    /// a household. Once they can, the two come apart: a head who leaves takes
    /// their headship with them and the children they consented to stay behind,
    /// so a family-shaped lookup would erase nothing and the records would go on
    /// being held under a consent whose giver had erased their account.
    /// </para>
    /// <para>
    /// The consent is the basis on which the record may exist at all
    /// (DPDP s.9), so the consent is what erasure has to follow. That is the
    /// rule this service already states — "their records exist on that person's
    /// parental consent, and consent that no longer exists cannot keep
    /// justifying the data it covered" — applied to the identifier that
    /// actually carries it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ChildProfile>> ListByConsentGiverAsync(
        Guid memberProfileId, CancellationToken cancellationToken = default);

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
