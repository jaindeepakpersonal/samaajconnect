using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.IdentityTenant.Domain.Authorization;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(60).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();

        // Seeded through the migration rather than by a startup routine, so the
        // schema and the reference data it depends on move together and a
        // freshly restored database is immediately usable.
        builder.HasData(AuthorizationCatalog.Roles);
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Key).HasMaxLength(100).IsRequired();
        builder.HasIndex(p => p.Key).IsUnique();

        builder.HasData(AuthorizationCatalog.Permissions);
    }
}

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });

        builder.HasOne<Role>().WithMany().HasForeignKey(rp => rp.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(rp => rp.PermissionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasData(AuthorizationCatalog.RolePermissions);
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(ur => ur.Id);

        // The aggregate assigns the id. Left as EF's default ValueGeneratedOnAdd,
        // a grant added to a *tracked* User comes back Modified rather than
        // Added and the save fails against a row that was never there. This is
        // the same trap member-family-service's CLAUDE.md records for Family and
        // FamilyMember; it stayed hidden here until GrantRole started adding a
        // role to a User that was already loaded.
        builder.Property(ur => ur.Id).ValueGeneratedNever();

        builder.Property(ur => ur.AssignedAt).IsRequired();

        builder.HasOne<Role>().WithMany().HasForeignKey(ur => ur.RoleId).OnDelete(DeleteBehavior.Restrict);

        // One grant per (user, role, scope). A null TenantScope is the
        // platform-wide grant, which is how Super Admin is represented.
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId, ur.TenantScope }).IsUnique();
    }
}
