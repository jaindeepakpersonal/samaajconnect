using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.MemberFamily.Domain.Families;

namespace Sangam.MemberFamily.Infrastructure.Persistence.Configurations;

public sealed class FamilyConfiguration : IEntityTypeConfiguration<Family>
{
    public void Configure(EntityTypeBuilder<Family> builder)
    {
        builder.ToTable("families");

        builder.HasKey(f => f.Id);

        // The aggregate assigns its own id. Left as the default
        // ValueGeneratedOnAdd, EF treats an already-set key as a hint that the
        // row exists, and a child added to a tracked parent comes back as
        // Modified instead of Added - an UPDATE against a row that is not there.
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.TenantId).IsRequired();
        builder.Property(f => f.FamilyHeadMemberId).IsRequired();
        builder.Property(f => f.FamilyCode).HasMaxLength(16).IsRequired();
        builder.Property(f => f.CreatedAt).IsRequired();

        // Unique per Samaaj, not platform-wide: two Samaaj may hand out the
        // same code without either being able to join the other's household.
        builder.HasIndex(f => new { f.TenantId, f.FamilyCode }).IsUnique();

        builder.HasMany(f => f.Members)
            .WithOne()
            .HasForeignKey(m => m.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Family.Members))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(f => f.DomainEvents);
    }
}

public sealed class FamilyMemberConfiguration : IEntityTypeConfiguration<FamilyMember>
{
    public void Configure(EntityTypeBuilder<FamilyMember> builder)
    {
        builder.ToTable("family_members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Relationship).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(m => m.RequestedAt).IsRequired();

        // One standing row per member per family. A member belongs to one
        // household, and this is what stops a duplicate request racing in.
        builder.HasIndex(m => new { m.FamilyId, m.MemberProfileId }).IsUnique();
        builder.HasIndex(m => m.MemberProfileId);
    }
}
