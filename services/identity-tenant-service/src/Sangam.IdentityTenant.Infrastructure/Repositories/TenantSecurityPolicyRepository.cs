using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Repositories;

public sealed class TenantSecurityPolicyRepository(IdentityTenantDbContext dbContext)
    : ITenantSecurityPolicyRepository
{
    public Task<TenantSecurityPolicy?> FindAsync(
        Guid tenantId, CancellationToken cancellationToken = default) =>
        dbContext.TenantSecurityPolicies.FirstOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);

    public void Add(TenantSecurityPolicy policy) => dbContext.TenantSecurityPolicies.Add(policy);
}
