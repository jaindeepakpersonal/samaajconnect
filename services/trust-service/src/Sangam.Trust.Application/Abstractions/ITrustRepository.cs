using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Abstractions;

// Trust is referenced as `Domain.Trusts.Trust` throughout, never bare and
// never via a `using Sangam.Trust.Domain.Trusts;` - "Trust" is also a
// namespace segment of this whole solution (Sangam.Trust.*), and the two
// resolve ambiguously in the type's own favour otherwise. boli-service hit
// the identical clash with `Boli` and settled on the same fully-qualified
// convention; see Sangam.Boli.Application.Abstractions.IBoliRepository.

public interface ITrustRepository
{
    /// <summary>Tenant-filtered.</summary>
    Task<Domain.Trusts.Trust?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Domain.Trusts.Trust?> FindByLocalityAsync(Guid localityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// For the Kafka consumer, which runs outside any request: there is no
    /// tenant context, so the query filter would compare against Guid.Empty
    /// and match nothing. Bypasses the filter and names the tenant - the one
    /// the event itself came from - explicitly instead.
    /// </summary>
    Task<Domain.Trusts.Trust?> FindByLocalityAsync(
        Guid tenantId, Guid localityId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Domain.Trusts.Trust>> ListAsync(
        Guid? localityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts on its own scope, returning false when the unique index on
    /// (TenantId, LocalityId) refuses a second Trust for a locality already
    /// claimed. This is the actual guarantee - a "does one already exist"
    /// read beforehand is a courtesy for a decent error message, not the
    /// mechanism, because two concurrent requests both pass it. The same
    /// shape as boli-service's bid index and panch-service's ballot index.
    /// </summary>
    Task<bool> TryAddAsync(Domain.Trusts.Trust trust, CancellationToken cancellationToken = default);
}

public interface ITrustMemberRepository
{
    /// <summary>Tenant-filtered.</summary>
    Task<TrustMember?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<TrustMember?> FindAsync(Guid trustId, Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrustMember>> ListByTrustAsync(Guid trustId, CancellationToken cancellationToken = default);

    void Add(TrustMember member);
}
