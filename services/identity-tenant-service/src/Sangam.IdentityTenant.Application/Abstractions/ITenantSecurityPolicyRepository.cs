using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// A Samaaj's own security-policy overrides. Sparse: most tenants have no row
/// at all, and <c>FindAsync</c> returning null means exactly that - use the
/// platform defaults, not "an error occurred".
/// </summary>
public interface ITenantSecurityPolicyRepository
{
    Task<TenantSecurityPolicy?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);

    void Add(TenantSecurityPolicy policy);
}
