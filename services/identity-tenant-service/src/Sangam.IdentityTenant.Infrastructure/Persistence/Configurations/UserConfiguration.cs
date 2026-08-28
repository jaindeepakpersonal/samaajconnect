using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.TenantId).IsRequired();
        builder.Property(u => u.MobileOrEmail).HasMaxLength(320).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.FailedLoginAttempts).IsRequired();
        builder.Property(u => u.IsContactVerified).IsRequired();

        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.AuthMethod).HasConversion<string>().HasMaxLength(20).IsRequired();

        // Unique platform-wide rather than per tenant: common login resolves an
        // identifier to exactly one Samaaj, which a per-tenant index would not
        // guarantee. See the remarks on the User aggregate.
        builder.HasIndex(u => u.MobileOrEmail).IsUnique();

        // The gateway and admin screens both list users a Samaaj at a time.
        builder.HasIndex(u => u.TenantId);

        builder.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(User.Roles))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(u => u.DomainEvents);
    }
}
