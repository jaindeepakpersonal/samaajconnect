using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Pathshala.Domain.Announcements;
using Sangam.Pathshala.Domain.Donations;

namespace Sangam.Pathshala.Infrastructure.Persistence.Configurations;

public sealed class PathshalaDonationConfiguration : IEntityTypeConfiguration<PathshalaDonation>
{
    public void Configure(EntityTypeBuilder<PathshalaDonation> builder)
    {
        builder.ToTable("pathshala_donations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DonorName).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Purpose).HasMaxLength(200);
        builder.Property(d => d.VoidReason).HasMaxLength(500);
        builder.Property(d => d.Mode).HasConversion<string>().HasMaxLength(20);

        // A receipt number is handed to a donor and has to point at exactly one
        // entry. The handler takes the next one; this is what holds when two
        // donations are recorded at once.
        builder.HasIndex(d => new { d.PathshalaId, d.ReceiptNumber }).IsUnique();

        builder.HasIndex(d => new { d.PathshalaId, d.DonatedOn });

        builder.Ignore(d => d.DomainEvents);
    }
}

public sealed class PathshalaAnnouncementConfiguration : IEntityTypeConfiguration<PathshalaAnnouncement>
{
    public void Configure(EntityTypeBuilder<PathshalaAnnouncement> builder)
    {
        builder.ToTable("pathshala_announcements");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Title).IsRequired().HasMaxLength(150);
        builder.Property(a => a.Body).IsRequired().HasMaxLength(2000);

        builder.HasIndex(a => new { a.PathshalaId, a.SentAt });

        builder.Ignore(a => a.DomainEvents);
    }
}
