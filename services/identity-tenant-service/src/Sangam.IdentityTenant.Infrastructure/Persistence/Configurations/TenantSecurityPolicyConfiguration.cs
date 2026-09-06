using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class TenantSecurityPolicyConfiguration : IEntityTypeConfiguration<TenantSecurityPolicy>
{
    public void Configure(EntityTypeBuilder<TenantSecurityPolicy> builder)
    {
        builder.ToTable("tenant_security_policies");

        // TenantId is the primary key, not a separate Id - one-to-one with
        // Tenant, and a row only exists once a Samaaj has customised
        // something.
        builder.HasKey(p => p.TenantId);
    }
}
