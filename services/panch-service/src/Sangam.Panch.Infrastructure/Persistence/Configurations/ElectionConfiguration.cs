using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sangam.Panch.Domain.Elections;
using Sangam.Panch.Domain.Panel;

namespace Sangam.Panch.Infrastructure.Persistence.Configurations;

public sealed class ElectionConfiguration : IEntityTypeConfiguration<PanchElection>
{
    public void Configure(EntityTypeBuilder<PanchElection> builder)
    {
        builder.ToTable("panch_elections");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ResultsVisibility).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(e => new { e.TenantId, e.Status });

        builder.HasMany(e => e.Candidates)
            .WithOne()
            .HasForeignKey(c => c.ElectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(PanchElection.Candidates))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(e => e.DomainEvents);
    }
}

public sealed class CandidateConfiguration : IEntityTypeConfiguration<Candidate>
{
    public void Configure(EntityTypeBuilder<Candidate> builder)
    {
        builder.ToTable("candidates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        // One candidacy per member per election. The aggregate refuses a second
        // nomination; this is what holds if two nominators arrive at once, and
        // two entries for one person would split their votes.
        builder.HasIndex(c => new { c.ElectionId, c.MemberId }).IsUnique();
    }
}

public sealed class BallotConfiguration : IEntityTypeConfiguration<Ballot>
{
    public void Configure(EntityTypeBuilder<Ballot> builder)
    {
        builder.ToTable("ballots");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        // ---------------------------------------------------------------
        // This index is the one-ballot-per-member guarantee.
        //
        // Not a check in CastBallotCommandHandler, which two concurrent
        // requests both pass, and not a lock. A voter's whole ballot (up to ten
        // choices) is inserted with this row in one transaction, so a second
        // submission violates this index and leaves nothing behind.
        //
        // Removing this index does not degrade the service. It breaks it, and
        // silently.
        // ---------------------------------------------------------------
        builder.HasIndex(b => new { b.ElectionId, b.VoterMemberId }).IsUnique();
    }
}

public sealed class VoteConfiguration : IEntityTypeConfiguration<Vote>
{
    public void Configure(EntityTypeBuilder<Vote> builder)
    {
        builder.ToTable("votes");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();

        // A voter backs each candidate at most once.
        builder.HasIndex(v => new { v.ElectionId, v.VoterMemberId, v.CandidateId }).IsUnique();

        // The tally: GROUP BY candidate within an election.
        builder.HasIndex(v => new { v.ElectionId, v.CandidateId });
    }
}

public sealed class ElectionResultConfiguration : IEntityTypeConfiguration<ElectionResult>
{
    /// <summary>
    /// Ordered ids as JSON. Npgsql will not serialise a collection without an
    /// explicit converter, and the comparer matters as much: without one EF
    /// compares by reference and never notices the list changed.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueConverter<IReadOnlyList<Guid>, string> Converter = new(
        ids => JsonSerializer.Serialize(ids, JsonOptions),
        json => JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions)!);

    private static ValueComparer<IReadOnlyList<Guid>> Comparer() => new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
        ids => ids.ToList());

    public void Configure(EntityTypeBuilder<ElectionResult> builder)
    {
        builder.ToTable("election_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // One result per election. Publishing twice must not produce two
        // rankings, because then "the result" has no referent.
        builder.HasIndex(r => r.ElectionId).IsUnique();

        builder.Property(r => r.RankedCandidateIds)
            .HasColumnType("jsonb")
            .HasConversion(Converter, Comparer());

        builder.Property(r => r.SeatedMemberIds)
            .HasColumnType("jsonb")
            .HasConversion(Converter, Comparer());
    }
}

public sealed class PanelMemberConfiguration : IEntityTypeConfiguration<PanelMember>
{
    public void Configure(EntityTypeBuilder<PanelMember> builder)
    {
        builder.ToTable("panel_members");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        // A member holds at most one seat in a Samaaj's panel.
        builder.HasIndex(m => new { m.TenantId, m.MemberId }).IsUnique();

        builder.Ignore(m => m.DomainEvents);
    }
}
