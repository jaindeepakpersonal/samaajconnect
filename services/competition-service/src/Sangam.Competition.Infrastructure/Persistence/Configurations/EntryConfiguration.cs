using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Infrastructure.Persistence.Configurations;

public sealed class EntryConfiguration : IEntityTypeConfiguration<Entry>
{
    public void Configure(EntityTypeBuilder<Entry> builder)
    {
        builder.ToTable("entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // One entry per participant per round. The handler checks this
        // first for a clean error message; this is what holds if two
        // requests arrive at once.
        builder.HasIndex(e => new { e.RoundId, e.ParticipantMemberId }).IsUnique();
    }
}

public sealed class ScoreConfiguration : IEntityTypeConfiguration<Score>
{
    public void Configure(EntityTypeBuilder<Score> builder)
    {
        builder.ToTable("scores");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Comments).HasMaxLength(500);

        // One mark per judge per entry - what makes "upsert" well-defined.
        // Not a never-amend guarantee like Vote's: a second write from the
        // same judge updates this row rather than being refused.
        builder.HasIndex(s => new { s.EntryId, s.JudgeMemberId }).IsUnique();
    }
}
