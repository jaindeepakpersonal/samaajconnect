using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Infrastructure.Persistence.Configurations;

public sealed class RoundResultConfiguration : IEntityTypeConfiguration<RoundResult>
{
    /// <summary>
    /// Ordered entry ids as JSON. Npgsql will not serialise a collection
    /// without an explicit converter, and the comparer matters as much:
    /// without one EF compares by reference and never notices the list
    /// changed. Mirrors celebrity-voting-service's own `CampaignResult`.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<RoundResult> builder)
    {
        builder.ToTable("round_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // One result per round. Publishing twice must not produce two
        // rankings, because then "the result" has no referent.
        builder.HasIndex(r => r.RoundId).IsUnique();

        builder.Property(r => r.RankedEntryIds)
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

public sealed class CompetitionResultConfiguration : IEntityTypeConfiguration<CompetitionResult>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<CompetitionResult> builder)
    {
        builder.ToTable("competition_results");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // One final result per competition, for the same reason as one per round.
        builder.HasIndex(r => r.CompetitionId).IsUnique();

        builder.Property(r => r.RankedMemberIds)
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
