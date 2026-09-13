using Sangam.Competition.Application.Competitions;
using Sangam.Competition.Domain.Entries;
using Xunit;

namespace Sangam.Competition.UnitTests;

/// <summary>
/// Covers `CompetitionMappings.RankBy` - the function that decides a round's
/// placement order.
/// </summary>
/// <remarks>
/// Mirrors celebrity-voting-service's own `CampaignMappingsTests` discipline:
/// entries are created deliberately out of chronological order, so the test
/// distinguishes "the tie-break by CreatedAt is actually doing the work"
/// from "the entries happened to already be in the right order". A test
/// built on entries created in order would pass even if the `ThenBy` were
/// deleted.
/// </remarks>
public sealed class CompetitionMappingsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid RoundId = Guid.NewGuid();

    [Fact]
    public void RankBy_orders_by_average_score_best_first()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var low = new Entry(TenantId, RoundId, Guid.NewGuid(), now.AddMinutes(3));
        var high = new Entry(TenantId, RoundId, Guid.NewGuid(), now.AddMinutes(1));
        var mid = new Entry(TenantId, RoundId, Guid.NewGuid(), now.AddMinutes(2));

        IReadOnlyList<Entry> entries = [low, high, mid];

        var averages = new Dictionary<Guid, double>
        {
            [low.Id] = 40,
            [high.Id] = 90,
            [mid.Id] = 65,
        };

        var ranked = entries.RankBy(averages);

        Assert.Equal([high.Id, mid.Id, low.Id], ranked.Select(e => e.Id));
    }

    [Fact]
    public void RankBy_breaks_a_tie_by_earliest_entry_first()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Deliberately created out of chronological order.
        var second = new Entry(TenantId, RoundId, Guid.NewGuid(), now.AddMinutes(5));
        var first = new Entry(TenantId, RoundId, Guid.NewGuid(), now.AddMinutes(1));

        IReadOnlyList<Entry> entries = [second, first];

        var averages = new Dictionary<Guid, double>
        {
            [second.Id] = 75,
            [first.Id] = 75,
        };

        var ranked = entries.RankBy(averages);

        Assert.Equal([first.Id, second.Id], ranked.Select(e => e.Id));
    }

    [Fact]
    public void RankBy_treats_an_unscored_entry_as_a_zero_average()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var scored = new Entry(TenantId, RoundId, Guid.NewGuid(), now.AddMinutes(2));
        var unscored = new Entry(TenantId, RoundId, Guid.NewGuid(), now.AddMinutes(1));

        IReadOnlyList<Entry> entries = [unscored, scored];

        var averages = new Dictionary<Guid, double> { [scored.Id] = 10 };

        var ranked = entries.RankBy(averages);

        Assert.Equal([scored.Id, unscored.Id], ranked.Select(e => e.Id));
    }
}
