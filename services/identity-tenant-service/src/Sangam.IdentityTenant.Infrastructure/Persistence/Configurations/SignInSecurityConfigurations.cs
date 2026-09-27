using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
{
    public void Configure(EntityTypeBuilder<TrustedDevice> builder)
    {
        builder.ToTable("trusted_devices");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.TokenHash).IsRequired().HasMaxLength(512);

        // Looked up by hash at sign-in; unique for the reason refresh_tokens' is.
        builder.HasIndex(d => d.TokenHash).IsUnique();

        // Revoking every device an account trusts.
        builder.HasIndex(d => new { d.UserId, d.RevokedAt });

        // Not tenant-scoped, like refresh_tokens: it is read before anybody has
        // signed in, and finding a row needs its 256-bit secret.
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CaptchaChallengeConfiguration : IEntityTypeConfiguration<CaptchaChallenge>
{
    public void Configure(EntityTypeBuilder<CaptchaChallenge> builder)
    {
        builder.ToTable("captcha_challenges");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.AnswerHash).IsRequired().HasMaxLength(512);

        // Purging the expired ones.
        builder.HasIndex(c => c.ExpiresAt);
    }
}
