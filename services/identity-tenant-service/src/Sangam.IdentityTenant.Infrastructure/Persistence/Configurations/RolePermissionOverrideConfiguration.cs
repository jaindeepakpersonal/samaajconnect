using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.IdentityTenant.Domain.Authorization;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class RolePermissionOverrideConfiguration
    : IEntityTypeConfiguration<RolePermissionOverride>
{
    public void Configure(EntityTypeBuilder<RolePermissionOverride> builder)
    {
        builder.ToTable("role_permission_overrides");
        builder.HasKey(o => o.Id);

        // Domain-assigned, like every other key in this service. Left as EF's
        // default, an override added to a tracked context comes back Modified
        // rather than Added and the save fails against a row that was never
        // there - the trap this repo has now hit on Family, UserRole and here.
        builder.Property(o => o.Id).ValueGeneratedNever();

        // One row per Samaaj, role and permission. The handler re-points an
        // existing override rather than stacking rows, and this is what holds
        // if two administrators click the same cell at once - without it the
        // effective matrix would depend on which row a query happened to read.
        builder.HasIndex(o => new { o.TenantId, o.RoleId, o.PermissionId }).IsUnique();

        builder.Ignore(o => o.DomainEvents);
    }
}
