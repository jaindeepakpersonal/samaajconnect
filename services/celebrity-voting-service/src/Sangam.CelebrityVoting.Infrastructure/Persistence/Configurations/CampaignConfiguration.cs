using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sangam.CelebrityVoting.Domain.Campaigns;

namespace Sangam.CelebrityVoting.Infrastructure.Persistence.Configurations;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<VotingCampaign>
{
    public void Configure(EntityTypeBuilder<VotingCampaign> builder)
    {
        builder.ToTable("voting_campaigns");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ResultsVisibility).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => new { c.TenantId, c.Status });

        builder.HasMany(c => c.Candidates)
            .WithOne()
            .HasForeignKey(c => c.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(VotingCampaign.Candidates))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.DomainEvents);
    }
}

public sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> builder)
    {
        builder.ToTable("candidates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Category).HasMaxLength(100);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        // One candidacy per member per campaign. The aggregate refuses a second
        // nomination; this is what holds if two nominators arrive at once, and
        // two entries for one person would split their vote.
        builder.HasIndex(c => new { c.CampaignId, c.MemberId }).IsUnique();
    }
}

public sealed class VoteConfiguration : IEntityTypeConfiguration<Vote>
{
    public void Configure(EntityTypeBuilder<Vote> builder)
    {
        builder.ToTable("votes");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        // ---------------------------------------------------------------
        // This index is the double-voting guarantee.
        //
        // Not the check in CastVoteCommandHandler, which two concurrent
        // requests both pass, and not a distributed lock, which has to decide
        // what to do when Redis is unreachable. SERVICES.md calls this a
        // correctness requirement rather than a nice-to-have, and it is right:
        // at the close of voting, two requests from one member arriving in the
        // same millisecond is the normal case.
        //
        // Removing this index does not degrade the service. It breaks it, and
        // silently.
        // ---------------------------------------------------------------
        builder.HasIndex(v => new { v.CampaignId, v.VoterMemberId }).IsUnique();

        // The tally: GROUP BY candidate within a campaign.
        builder.HasIndex(v => new { v.CampaignId, v.CandidateId });
    }
}

public sealed class CampaignResultConfiguration : IEntityTypeConfiguration<CampaignResult>
{
    /// <summary>
    /// Ordered candidate ids as JSON. Npgsql will not serialise a collection
    /// without an explicit converter, and the comparer matters as much: without
    /// one EF compares by reference and never notices the list changed.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<CampaignResult> builder)
    {
        builder.ToTable("campaign_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // One result per campaign. Publishing twice must not produce two
        // rankings, because then "the result" has no referent.
        builder.HasIndex(r => r.CampaignId).IsUnique();

        builder.Property(r => r.RankedCandidateIds)
            .HasColumnType("jsonb")
            .HasConversion(
                new ValueConverter<IReadOnlyList<Guid>, string>(
                    ids => JsonSerializer.Serialize(ids, JsonOptions),
                    json => JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions)!),
                new ValueComparer<IReadOnlyList<Guid>>(
                    (left, right) => left != null && right != null && left.SequenceEqual(right),
                    ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
                    ids => ids.ToList()));
    }
}
