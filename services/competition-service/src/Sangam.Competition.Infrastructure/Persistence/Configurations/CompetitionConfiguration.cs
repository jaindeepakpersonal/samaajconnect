using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Competition.Domain.Competitions;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.Infrastructure.Persistence.Configurations;

public sealed class CompetitionConfiguration : IEntityTypeConfiguration<CompetitionAggregate>
{
    public void Configure(EntityTypeBuilder<CompetitionAggregate> builder)
    {
        builder.ToTable("competitions");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.Venue).HasMaxLength(300);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);

        builder.HasIndex(c => new { c.TenantId, c.Status });
        builder.HasIndex(c => new { c.TenantId, c.LocalityId });

        builder.HasMany(c => c.Registrations)
            .WithOne()
            .HasForeignKey(r => r.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Judges)
            .WithOne()
            .HasForeignKey(j => j.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Rounds)
            .WithOne()
            .HasForeignKey(r => r.CompetitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(CompetitionAggregate.Registrations))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(CompetitionAggregate.Judges))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(CompetitionAggregate.Rounds))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.DomainEvents);
    }
}

public sealed class CompetitionRegistrationConfiguration : IEntityTypeConfiguration<CompetitionRegistration>
{
    public void Configure(EntityTypeBuilder<CompetitionRegistration> builder)
    {
        builder.ToTable("competition_registrations");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        // One registration row per member per competition; a withdrawal
        // reactivates the same row rather than inserting a second one.
        builder.HasIndex(r => new { r.CompetitionId, r.MemberId }).IsUnique();
    }
}

public sealed class CompetitionJudgeConfiguration : IEntityTypeConfiguration<CompetitionJudge>
{
    public void Configure(EntityTypeBuilder<CompetitionJudge> builder)
    {
        builder.ToTable("competition_judges");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();

        builder.HasIndex(j => new { j.CompetitionId, j.MemberId }).IsUnique();
    }
}

public sealed class RoundConfiguration : IEntityTypeConfiguration<Round>
{
    public void Configure(EntityTypeBuilder<Round> builder)
    {
        builder.ToTable("rounds");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(r => new { r.CompetitionId, r.SequenceNumber }).IsUnique();
    }
}
