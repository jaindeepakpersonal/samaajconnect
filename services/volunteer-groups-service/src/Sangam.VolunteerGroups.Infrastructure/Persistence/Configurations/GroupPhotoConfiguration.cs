using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.VolunteerGroups.Domain.Groups;
using Sangam.VolunteerGroups.Domain.Media;

namespace Sangam.VolunteerGroups.Infrastructure.Persistence.Configurations;

public sealed class GroupPhotoConfiguration : IEntityTypeConfiguration<GroupPhoto>
{
    public void Configure(EntityTypeBuilder<GroupPhoto> builder)
    {
        builder.ToTable("group_photos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Bytes).IsRequired();
        builder.Property(p => p.ContentHash).IsRequired().HasMaxLength(64);

        // Deleting a group takes its photo with it; nothing else points here.
        builder.HasOne<VolunteerGroup>()
            .WithMany()
            .HasForeignKey(p => p.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.GroupId });
    }
}
