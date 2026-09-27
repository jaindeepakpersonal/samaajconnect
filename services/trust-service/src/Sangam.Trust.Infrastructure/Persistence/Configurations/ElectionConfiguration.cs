using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Trust.Domain.Elections;

namespace Sangam.Trust.Infrastructure.Persistence.Configurations;

public sealed class ElectionConfiguration : IEntityTypeConfiguration<TrustElection>
{
    public void Configure(EntityTypeBuilder<TrustElection> builder)
    {
        builder.ToTable("trust_elections");
        builder.HasKey(e => e.Id);

        // Domain-assigned, like every key on this platform.
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.TrustId, e.Status });
        builder.HasIndex(e => e.RoleId);

        // A uuid[] in ranked order, like the other services' loose id lists.
        builder.Property(e => e.WinnerMemberIds).HasColumnType("uuid[]");

        builder.HasMany(e => e.Candidates)
            .WithOne()
            .HasForeignKey(c => c.ElectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(TrustElection.Candidates))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(e => e.DomainEvents);
    }
}

public sealed class TrustElectionCandidateConfiguration : IEntityTypeConfiguration<TrustElectionCandidate>
{
    public void Configure(EntityTypeBuilder<TrustElectionCandidate> builder)
    {
        builder.ToTable("trust_election_candidates");
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
        builder.ToTable("trust_election_votes");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        // ---------------------------------------------------------------
        // These two indexes are the one-ballot guarantee, not the handler's
        // own courtesy check and not a distributed lock. A ballot is one row
        // per pick, numbered from 1: the first holds one ballot per voter
        // (every ballot has a choice 1, so a second collides on it), the
        // second one mark per candidate on it. See CastTrustVoteCommand's
        // remarks, and volunteer-groups-service's own VoteConfiguration.
        // ---------------------------------------------------------------
        builder.HasIndex(v => new { v.ElectionId, v.VoterMemberId, v.Choice }).IsUnique();
        builder.HasIndex(v => new { v.ElectionId, v.VoterMemberId, v.CandidateId }).IsUnique();

        // The tally: GROUP BY candidate within an election.
        builder.HasIndex(v => new { v.ElectionId, v.CandidateId });
    }
}
