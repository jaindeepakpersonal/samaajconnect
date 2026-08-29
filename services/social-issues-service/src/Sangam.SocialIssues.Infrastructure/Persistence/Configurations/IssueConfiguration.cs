using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.SocialIssues.Domain.Issues;

namespace Sangam.SocialIssues.Infrastructure.Persistence.Configurations;

public sealed class IssueConfiguration : IEntityTypeConfiguration<SocialIssue>
{
    public void Configure(EntityTypeBuilder<SocialIssue> builder)
    {
        builder.ToTable("social_issues");
        builder.HasKey(i => i.Id);

        // Domain-assigned, like every key on this platform. Left as EF's
        // default, a child added to a tracked parent comes back Modified rather
        // than Added and the save fails against a row that was never there.
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.Title).IsRequired().HasMaxLength(200);
        builder.Property(i => i.Description).IsRequired().HasMaxLength(5000);
        builder.Property(i => i.Category).IsRequired().HasMaxLength(50);
        builder.Property(i => i.Locality).HasMaxLength(150);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);

        // The published list, and the reviewer's queue: both are "this Samaaj's
        // issues in these statuses, in date order".
        builder.HasIndex(i => new { i.TenantId, i.Status, i.CreatedAt });

        // "My Submissions".
        builder.HasIndex(i => new { i.TenantId, i.SubmittedByMemberId });

        builder.HasMany(i => i.History)
            .WithOne()
            .HasForeignKey(h => h.IssueId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(SocialIssue.History))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(i => i.DomainEvents);
    }
}

public sealed class IssueStatusHistoryConfiguration : IEntityTypeConfiguration<IssueStatusHistory>
{
    public void Configure(EntityTypeBuilder<IssueStatusHistory> builder)
    {
        builder.ToTable("issue_status_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.Reason).HasMaxLength(1000);

        // Read in order, always.
        builder.HasIndex(h => new { h.IssueId, h.CreatedAt });
    }
}
