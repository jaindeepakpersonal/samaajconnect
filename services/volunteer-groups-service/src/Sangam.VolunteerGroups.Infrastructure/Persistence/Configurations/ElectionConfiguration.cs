using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.VolunteerGroups.Domain.Elections;

namespace Sangam.VolunteerGroups.Infrastructure.Persistence.Configurations;

public sealed class ElectionConfiguration : IEntityTypeConfiguration<GroupElection>
{
    public void Configure(EntityTypeBuilder<GroupElection> builder)
    {
        builder.ToTable("group_elections");
        builder.HasKey(e => e.Id);

        // Domain-assigned, like every key on this platform.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.PositionTitle).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.GroupId, e.Status });

        builder.HasMany(e => e.Candidates)
            .WithOne()
            .HasForeignKey(c => c.ElectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(GroupElection.Candidates))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(e => e.DomainEvents);
    }
}

public sealed class ElectionCandidateConfiguration : IEntityTypeConfiguration<ElectionCandidate>
{
    public void Configure(EntityTypeBuilder<ElectionCandidate> builder)
    {
        builder.ToTable("election_candidates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        // One candidacy per member per election - the aggregate refuses a
        // second nomination; this is what holds if two nominators arrive at
        // once, and two entries for one person would split their vote.
        builder.HasIndex(c => new { c.ElectionId, c.MemberId }).IsUnique();
    }
}

public sealed class VoteConfiguration : IEntityTypeConfiguration<Vote>
{
    public void Configure(EntityTypeBuilder<Vote> builder)
    {
        builder.ToTable("election_votes");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        // ---------------------------------------------------------------
        // This index is the double-voting guarantee, not the handler's own
        // courtesy check and not a distributed lock. See CastVoteCommand's
        // remarks, and celebrity-voting-service's own VoteConfiguration,
        // which this copies verbatim at a smaller scale.
        // ---------------------------------------------------------------
        builder.HasIndex(v => new { v.ElectionId, v.VoterMemberId }).IsUnique();

        // The tally: GROUP BY candidate within an election.
        builder.HasIndex(v => new { v.ElectionId, v.CandidateId });
    }
}
