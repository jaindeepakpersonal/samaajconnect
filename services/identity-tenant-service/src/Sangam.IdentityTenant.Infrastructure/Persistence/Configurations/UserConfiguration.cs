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

        builder.Property(u => u.ConvertedFromChildProfileId);

        // Owned rather than a table of its own: a code has no life apart from
        // the account it belongs to, and is deleted with it.
        builder.OwnsOne(u => u.ActivationCode, code =>
        {
            code.Property(c => c.Hash).HasColumnName("activation_code_hash").HasMaxLength(512);
            code.Property(c => c.IssuedAt).HasColumnName("activation_code_issued_at");
            code.Property(c => c.ExpiresAt).HasColumnName("activation_code_expires_at");
            code.Property(c => c.IssuedBy).HasColumnName("activation_code_issued_by");
            code.Property(c => c.FailedAttempts).HasColumnName("activation_code_failed_attempts");
        });

        builder.OwnsOne(u => u.LoginOtp, otp =>
        {
            otp.Property(c => c.Hash).HasColumnName("login_otp_hash").HasMaxLength(512);
            otp.Property(c => c.IssuedAt).HasColumnName("login_otp_issued_at");
            otp.Property(c => c.ExpiresAt).HasColumnName("login_otp_expires_at");
        });

        builder.OwnsOne(u => u.PasswordResetCode, code =>
        {
            code.Property(c => c.Hash).HasColumnName("password_reset_code_hash").HasMaxLength(512);
            code.Property(c => c.IssuedAt).HasColumnName("password_reset_code_issued_at");
            code.Property(c => c.ExpiresAt).HasColumnName("password_reset_code_expires_at");
        });

        // The admin's pending list, and the consumer's idempotency check.
        builder.HasIndex(u => new { u.TenantId, u.Status });
        builder.HasIndex(u => u.ConvertedFromChildProfileId);

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
