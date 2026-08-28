using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);

        // Domain-assigned, like every other key here. Left as EF's default a
        // token added to a tracked graph comes back Modified rather than Added
        // - see the note on UserRole.
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(512);

        // The redeem path looks a token up by hash and nothing else, so this
        // index is the whole read pattern. Unique because two rows sharing a
        // hash would make "which session is this?" unanswerable.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Revoking a session walks its chain.
        builder.HasIndex(t => t.SessionId);

        // Ending every session for one account, and the erasure path.
        builder.HasIndex(t => new { t.UserId, t.RevokedAt });

        builder.Property(t => t.RevokedReason).HasConversion<string>().HasMaxLength(40);
    }
}
