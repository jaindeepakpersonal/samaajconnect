using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Infrastructure.Persistence.Configurations;

public sealed class TrustMemberConfiguration : IEntityTypeConfiguration<TrustMember>
{
    public void Configure(EntityTypeBuilder<TrustMember> builder)
    {
        builder.ToTable("trust_members");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);

        // At most one row per (Trust, Member) ever - a removed member is
        // reactivated on this same row rather than inserted again. See
        // TrustMember.Reactivate.
        builder.HasIndex(m => new { m.TrustId, m.MemberId }).IsUnique();

        // A role's holders - what its seats are counted against.
        builder.HasIndex(m => new { m.TrustId, m.RoleId });

        builder.Ignore(m => m.DomainEvents);
    }
}
