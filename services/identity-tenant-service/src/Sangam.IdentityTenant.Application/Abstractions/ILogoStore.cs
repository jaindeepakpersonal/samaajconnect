using Sangam.IdentityTenant.Domain.Media;

namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// Where the bytes of a Samaaj's logo live.
/// </summary>
/// <remarks>
/// This service's own Postgres, for the reason member-family-service keeps
/// member photos in its: at this scale an object store buys nothing and costs a
/// second place data lives, one that <c>scripts/backup-restore-drill.sh</c> does
/// not dump. The interface is what makes changing that one implementation and a
/// migration rather than a rewrite of the handlers.
///
/// Nothing here is tenant-filtered, and that is not an omission.
/// <c>TenantLogo</c> is not <c>ITenantScopedEntity</c> because <c>Tenant</c> is
/// not either — it is the row every other entity's TenantId points at. A logo is
/// also read on the anonymous registration path, where no tenant is resolved at
/// all, so a query filter would hide every logo from the one screen that most
/// needs them.
/// </remarks>
public interface ILogoStore
{
    Task<TenantLogo?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(TenantLogo logo);

    /// <summary>
    /// Removes a logo. Silent when the id is unknown: a logo replaced twice, or
    /// removed by two administrators at once, must not fail.
    /// </summary>
    Task RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every logo a Samaaj has, whatever the tenant row points at.
    /// </summary>
    /// <remarks>
    /// For archiving a Samaaj. Deleting only the id the row holds would leave
    /// behind anything a replace path had orphaned.
    /// </remarks>
    Task RemoveAllForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
