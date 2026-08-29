using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.VolunteerGroups.Domain.Groups;

namespace Sangam.VolunteerGroups.Infrastructure.Persistence.Configurations;

public sealed class VolunteerGroupConfiguration : IEntityTypeConfiguration<VolunteerGroup>
{
    public void Configure(EntityTypeBuilder<VolunteerGroup> builder)
    {
        builder.ToTable("volunteer_groups");
        builder.HasKey(g => g.Id);

        // Domain-assigned, like every key on this platform. Left as EF's
        // default, a child added to a tracked parent comes back Modified rather
        // than Added and the save fails against a row that was never there.
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.Name).IsRequired().HasMaxLength(150);
        builder.Property(g => g.Description).HasMaxLength(2000);
        builder.Property(g => g.FocusArea).HasMaxLength(100);
        builder.Property(g => g.Status).HasConversion<string>().HasMaxLength(20);

        // One group per name per Samaaj. The handler checks first for a decent
        // error message; this is what holds if two admins create at once.
        builder.HasIndex(g => new { g.TenantId, g.Name }).IsUnique();

        builder.HasIndex(g => new { g.TenantId, g.Status });

        builder.HasMany(g => g.Applications)
            .WithOne()
            .HasForeignKey(a => a.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(g => g.Members)
            .WithOne()
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        foreach (var navigation in new[]
                 {
                     nameof(VolunteerGroup.Applications),
                     nameof(VolunteerGroup.Members),
                 })
        {
            builder.Metadata.FindNavigation(navigation)!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        }

        builder.Ignore(g => g.DomainEvents);
    }
}

public sealed class GroupApplicationConfiguration : IEntityTypeConfiguration<GroupApplication>
{
    public void Configure(EntityTypeBuilder<GroupApplication> builder)
    {
        builder.ToTable("group_applications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Note).HasMaxLength(1000);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        // One live application per member per group. The aggregate replaces a
        // rejected one rather than adding a second, so this is what that
        // invariant looks like in the database.
        builder.HasIndex(a => new { a.GroupId, a.MemberId }).IsUnique();
    }
}

public sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("group_members");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.RolePosition).HasMaxLength(100);

        builder.HasIndex(m => new { m.GroupId, m.MemberId }).IsUnique();

        // "Which groups am I in?" - the member-portal's own view of this.
        builder.HasIndex(m => m.MemberId);
    }
}
