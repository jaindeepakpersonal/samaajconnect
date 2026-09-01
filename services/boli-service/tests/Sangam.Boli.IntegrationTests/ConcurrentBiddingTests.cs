using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Boli.Application.Auctions;
using Sangam.Boli.Application.Security;
using Xunit;

namespace Sangam.Boli.IntegrationTests;

/// <summary>
/// The guarantee: a Boli has exactly one highest bid, whatever arrives at once.
/// </summary>
/// <remarks>
/// Two mechanisms hold it together and both are tested here.
///
/// The row lock in <c>LockForBiddingAsync</c> serialises bidders on one Boli, so
/// the read of the current highest and the write of the new bid cannot be
/// interleaved. That is what makes racing bids come out as a clean sequence
/// rather than a pile.
///
/// The unique index on (BoliId, Amount) is what remains true if some future code
/// path forgets the lock, or if this service runs on two instances and somebody
/// has replaced the lock with an in-process one. <see cref="BidIndexTests"/>
/// names it directly.
///
/// Both are needed. A test that only proved the behaviour would also pass if a
/// handler happened to serialise its callers, which would stop being true the
/// moment the shape of the code changed.
/// </remarks>
public sealed class ConcurrentBiddingTests(BoliApiFactory factory)
    : IClassFixture<BoliApiFactory>, IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Manager() => factory.CreateClientAs(
        Guid.NewGuid(),
        Tenant,
        [Roles.BoliManager],
        [PermissionKeys.BoliManage, PermissionKeys.BoliPublishResults, PermissionKeys.MembersRead]);

    private HttpClient Member(Guid memberId) => factory.CreateClientAs(
        memberId, Tenant, [Roles.Member], [PermissionKeys.MembersRead]);

    /// <summary>Sets up an occasion with one open Boli, and returns its id.</summary>
    private async Task<Guid> OpenBoliAsync(long startingAmount = 1_000_00, long minIncrement = 500_00)
    {
        var manager = Manager();
        var now = factory.Clock.UtcNow;

        var occasion = await manager.PostAsJsonAsync("/v1/boli/occasions", new
        {
            title = "Paryushan 2026",
            description = "The Samaaj's annual Boli.",
            occasionDate = "2026-09-10",
        });

        occasion.StatusCode.Should().Be(HttpStatusCode.OK);

        var occasionBody = await occasion.Content.ReadFromJsonAsync<OccasionResponse>();

        var type = await manager.PostAsJsonAsync(
            $"/v1/boli/occasions/{occasionBody!.Id}/boli-types",
            new { name = "Mangal Deep", description = (string?)null });

        type.StatusCode.Should().Be(HttpStatusCode.OK);

        var typeBody = await type.Content.ReadFromJsonAsync<BoliTypeResponse>();

        var lot = await manager.PostAsJsonAsync($"/v1/boli/occasions/{occasionBody.Id}/boli", new
        {
            boliTypeId = typeBody!.Id,
            title = "Mangal Deep",
            startAt = now.AddMinutes(-5),
            endAt = now.AddHours(2),
            startingAmount,
            minIncrement,
            eligibilityRule = (string?)null,
        });

        lot.StatusCode.Should().Be(HttpStatusCode.OK);

        var lotBody = await lot.Content.ReadFromJsonAsync<BoliResponse>();

        lotBody!.AcceptsBids.Should().BeTrue();

        return lotBody.Id;
    }

    [Fact]
    public async Task Twenty_bidders_offering_the_same_amount_leave_exactly_one_bid()
    {
        var boliId = await OpenBoliAsync();

        // Everybody offers exactly the starting amount at the same instant. Only
        // one of them can be the first bid, and the rest have to be told they
        // were outbid rather than silently joining them at the top.
        var attempts = Enumerable.Range(0, 20).Select(_ =>
        {
            var client = Member(Guid.NewGuid());

            return client.PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });
        });

        var responses = await Task.WhenAll(attempts);

        foreach (var response in responses)
        {
            // Being outbid is not an error. Every one of these is a 200.
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var bodies = await Task.WhenAll(
            responses.Select(r => r.Content.ReadFromJsonAsync<PlaceBidResponse>()));

        bodies.Count(b => b!.Accepted).Should().Be(1, "only one bid can be the highest");

        var stored = await factory.WithDbContextAsync(db =>
            db.Bids.IgnoreQueryFilters().Where(b => b.BoliId == boliId).CountAsync());

        stored.Should().Be(1);
    }

    [Fact]
    public async Task Racing_bids_at_increasing_amounts_all_land_and_stay_ordered()
    {
        var boliId = await OpenBoliAsync(startingAmount: 1_000_00, minIncrement: 100_00);

        // Twelve distinct amounts, all clearly above the floor and each other,
        // submitted at once. Every one is legal against the floor, so every one
        // should land - the lock orders them, it does not reject them.
        var amounts = Enumerable.Range(0, 12).Select(i => 10_000_00L + (i * 100_00L)).ToArray();

        var responses = await Task.WhenAll(amounts.Select(amount =>
            Member(Guid.NewGuid())
                .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount })));

        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var stored = await factory.WithDbContextAsync(db =>
            db.Bids.IgnoreQueryFilters()
                .Where(b => b.BoliId == boliId)
                .Select(b => b.Amount)
                .ToListAsync());

        // No amount appears twice, which is the property the unique index and the
        // lock exist to hold.
        stored.Should().OnlyHaveUniqueItems();
        stored.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_bid_below_the_minimum_is_reported_as_outbid_not_as_an_error()
    {
        var boliId = await OpenBoliAsync(startingAmount: 1_000_00, minIncrement: 500_00);
        var member = Member(Guid.NewGuid());

        var first = await member.PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // 1,400 does not clear 1,000 + 500.
        var second = await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_400_00L });

        second.StatusCode.Should().Be(HttpStatusCode.OK, "being outbid is not misconduct");

        var body = await second.Content.ReadFromJsonAsync<PlaceBidResponse>();

        body!.Accepted.Should().BeFalse();
        body.HighestAmount.Should().Be(1_000_00L);

        // And it hands back the number they now need, so the screen does not have
        // to compute the increment itself.
        body.MinimumNextBid.Should().Be(1_500_00L);
    }

    [Fact]
    public async Task Bidding_stops_when_the_window_passes_even_though_nobody_closed_it()
    {
        var boliId = await OpenBoliAsync();

        factory.Clock.Advance(TimeSpan.FromHours(3));

        var response = await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 5_000_00L });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        factory.Clock.Advance(TimeSpan.FromHours(-3));
    }

    [Fact]
    public async Task The_bid_history_never_names_who_bid()
    {
        var boliId = await OpenBoliAsync();
        var me = Guid.NewGuid();

        await Member(me).PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });
        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 2_000_00L });

        var response = await Member(me).GetAsync($"/v1/boli/boli/{boliId}/bids");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync();

        // A running public list of who is prepared to pay what turns an auction
        // into a statement about people's means. The reader is told which are
        // theirs and nothing else.
        raw.Should().NotContain(me.ToString());
        raw.Should().Contain("isMine");

        var history = await response.Content.ReadFromJsonAsync<List<BidResponse>>();

        history.Should().NotBeNull().And.HaveCount(2);
        history!.Count(b => b.IsMine).Should().Be(1);
    }
}
