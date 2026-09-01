using FluentAssertions;
using Sangam.Boli.Domain.Auctions;
using Xunit;

namespace Sangam.Boli.UnitTests;

/// <summary>
/// The Boli as a thing that runs over time: the window, the floor, the
/// increment, and the two-step close-then-publish.
/// </summary>
public sealed class BoliTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static Domain.Auctions.Boli Open(
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null,
        long startingAmount = 1_000_00,
        long minIncrement = 500_00)
    {
        var lot = Domain.Auctions.Boli.Open(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Mangal Deep",
            startAt ?? Noon.AddHours(-1),
            endAt ?? Noon.AddHours(1),
            startingAmount,
            minIncrement,
            eligibilityRule: null,
            Noon.AddDays(-1));

        lot.Start();

        return lot;
    }

    [Fact]
    public void A_Boli_starts_scheduled_and_takes_no_bids_until_it_is_started()
    {
        var lot = Domain.Auctions.Boli.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Aarti",
            Noon.AddHours(-1), Noon.AddHours(1), 1_000_00, 500_00, null, Noon.AddDays(-1));

        lot.Status.Should().Be(BoliStatus.Scheduled);
        lot.AcceptsBids(Noon).Should().BeFalse();
    }

    [Fact]
    public void The_status_alone_does_not_mean_it_is_taking_bids()
    {
        // Open, but its window closed an hour ago because nobody clicked Close.
        var lot = Open(startAt: Noon.AddHours(-3), endAt: Noon.AddHours(-1));

        lot.Status.Should().Be(BoliStatus.Open);
        lot.AcceptsBids(Noon).Should().BeFalse();
    }

    [Fact]
    public void Nor_does_it_take_bids_before_its_window_arrives()
    {
        var lot = Open(startAt: Noon.AddHours(1), endAt: Noon.AddHours(3));

        lot.AcceptsBids(Noon).Should().BeFalse();
    }

    [Fact]
    public void The_first_bid_has_to_meet_the_starting_amount()
    {
        var lot = Open(startingAmount: 1_000_00);

        lot.MinimumNextBid(null).Should().Be(1_000_00);
        lot.IsAcceptable(999_00, null).Should().BeFalse();
        lot.IsAcceptable(1_000_00, null).Should().BeTrue();
    }

    [Fact]
    public void Every_later_bid_has_to_clear_the_highest_by_the_increment()
    {
        // The wireframe's "Minimum ₹15,600" against a ₹15,100 high.
        var lot = Open(startingAmount: 10_000_00, minIncrement: 500_00);

        lot.MinimumNextBid(15_100_00).Should().Be(15_600_00);
        lot.IsAcceptable(15_599_00, 15_100_00).Should().BeFalse();
        lot.IsAcceptable(15_600_00, 15_100_00).Should().BeTrue();
    }

    [Fact]
    public void Closing_is_idempotent_because_the_close_races_a_clock()
    {
        var lot = Open();

        lot.Close(Noon).Should().BeTrue();
        lot.Close(Noon.AddMinutes(1)).Should().BeTrue();

        lot.Status.Should().Be(BoliStatus.Closed);
        lot.ClosedAt.Should().Be(Noon);
    }

    [Fact]
    public void A_closed_Boli_takes_no_more_bids()
    {
        var lot = Open();

        lot.Close(Noon);

        lot.AcceptsBids(Noon).Should().BeFalse();
    }

    [Fact]
    public void A_result_cannot_be_published_before_the_bidding_is_closed()
    {
        var lot = Open();

        lot.MarkPublished(Guid.NewGuid(), 1_000_00, Noon).Should().BeFalse();
        lot.Status.Should().Be(BoliStatus.Open);
    }

    [Fact]
    public void Publishing_moves_it_once_and_raises_the_announcement()
    {
        var lot = Open();
        var winner = Guid.NewGuid();

        lot.Close(Noon);
        lot.MarkPublished(winner, 15_600_00, Noon.AddMinutes(5)).Should().BeTrue();

        lot.Status.Should().Be(BoliStatus.ResultPublished);

        // Who won and for how much: unlike a celebrity-voting ranking, winning a
        // Boli is a public act with a payment attached, so a downstream receipt
        // needs both.
        lot.DomainEvents.Should().ContainSingle(e => e.Topic == "boli.result.published.v1");
    }

    [Fact]
    public void Publishing_twice_is_refused_rather_than_announced_twice()
    {
        var lot = Open();

        lot.Close(Noon);
        lot.MarkPublished(Guid.NewGuid(), 1_000_00, Noon).Should().BeTrue();
        lot.MarkPublished(Guid.NewGuid(), 9_999_00, Noon).Should().BeFalse();
    }
}

public sealed class BoliOccasionTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static BoliOccasion Create() =>
        BoliOccasion.Create(
            Guid.NewGuid(),
            "Paryushan 2026",
            "The Samaaj's annual Boli.",
            new DateOnly(2026, 9, 10),
            Noon);

    [Fact]
    public void An_occasion_starts_upcoming()
    {
        Create().Status.Should().Be(OccasionStatus.Upcoming);
    }

    [Fact]
    public void Two_types_cannot_share_a_name_however_it_is_cased()
    {
        // Two "Mangal Deep" types would leave every published result ambiguous
        // about which one a Boli belonged to.
        var occasion = Create();

        occasion.DefineType("Mangal Deep", null).Should().NotBeNull();
        occasion.DefineType("mangal deep", null).Should().BeNull();

        occasion.Types.Should().HaveCount(1);
    }

    [Fact]
    public void An_occasion_does_not_reopen_once_it_is_closed()
    {
        var occasion = Create();

        occasion.MoveTo(OccasionStatus.Closed, Noon).Should().BeTrue();
        occasion.MoveTo(OccasionStatus.Active, Noon).Should().BeFalse();

        occasion.Status.Should().Be(OccasionStatus.Closed);
    }

    [Fact]
    public void Moving_to_where_it_already_is_is_not_an_error()
    {
        var occasion = Create();

        occasion.MoveTo(OccasionStatus.Upcoming, Noon).Should().BeTrue();
    }
}

public sealed class BoliResultTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_recorded_result_is_not_a_published_one()
    {
        var bid = Bid.Place(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15_600_00, Noon);
        var result = BoliResult.Record(Guid.NewGuid(), Guid.NewGuid(), bid, Guid.NewGuid(), Noon);

        result.IsPublished.Should().BeFalse();
        result.PublishedAt.Should().BeNull();
    }

    [Fact]
    public void Publishing_twice_keeps_the_first_announcement_intact()
    {
        // A retried request must be safe, and a repeat must not quietly reassign
        // who announced it.
        var bid = Bid.Place(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15_600_00, Noon);
        var result = BoliResult.Record(Guid.NewGuid(), Guid.NewGuid(), bid, Guid.NewGuid(), Noon);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        result.Publish(first, Noon).Should().BeTrue();
        result.Publish(second, Noon.AddHours(1)).Should().BeTrue();

        result.PublishedBy.Should().Be(first);
        result.PublishedAt.Should().Be(Noon);
    }

    [Fact]
    public void The_amount_is_copied_off_the_winning_bid_so_the_result_stands_alone()
    {
        var bid = Bid.Place(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 15_600_00, Noon);
        var result = BoliResult.Record(Guid.NewGuid(), Guid.NewGuid(), bid, Guid.NewGuid(), Noon);

        result.Amount.Should().Be(15_600_00);
        result.WinningBidId.Should().Be(bid.Id);
        result.WinningMemberId.Should().Be(bid.MemberId);
    }
}
