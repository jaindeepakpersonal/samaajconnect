using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sangam.Boli.Application.Auctions;
using Sangam.Boli.Application.Security;
using Xunit;

namespace Sangam.Boli.IntegrationTests;

/// <summary>
/// Who bid and when, for the Samaaj's managers only, and where a Boli is held -
/// a venue and a city or village beside its occasion's locality - carried on
/// every Boli and result so a member can find them by place (2026-10-24).
/// </summary>
public sealed class BidLedgerAndPlaceTests(BoliApiFactory factory)
    : IClassFixture<BoliApiFactory>, IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Manager() => factory.CreateClientAs(
        ManagerId, Tenant, [Roles.BoliManager],
        [PermissionKeys.BoliManage, PermissionKeys.BoliPublishResults, PermissionKeys.MembersRead]);

    private HttpClient Member(Guid memberId) => factory.CreateClientAs(
        memberId, Tenant, [Roles.Member], [PermissionKeys.MembersRead]);

    private async Task<(Guid OccasionId, Guid BoliId)> OpenBoliAsync(
        string title, Guid? localityId = null, Guid? cityId = null, string? venue = null)
    {
        var manager = Manager();
        var now = factory.Clock.UtcNow;

        var occasion = await (await manager.PostAsJsonAsync("/v1/boli/occasions", new
        {
            title,
            description = (string?)null,
            occasionDate = "2026-09-10",
            localityId,
            venue,
            cityId,
        })).Content.ReadFromJsonAsync<OccasionResponse>();

        var type = await (await manager.PostAsJsonAsync(
                $"/v1/boli/occasions/{occasion!.Id}/boli-types", new { name = "Mangal Deep", description = (string?)null }))
            .Content.ReadFromJsonAsync<BoliTypeResponse>();

        var lot = await (await manager.PostAsJsonAsync($"/v1/boli/occasions/{occasion.Id}/boli", new
        {
            boliTypeId = type!.Id,
            title = title + " lot",
            startAt = now.AddMinutes(-5),
            endAt = now.AddHours(2),
            startingAmount = 1_000_00L,
            minIncrement = 500_00L,
            eligibilityRule = (string?)null,
        })).Content.ReadFromJsonAsync<BoliResponse>();

        return (occasion.Id, lot!.Id);
    }

    [Fact]
    public async Task A_manager_sees_who_bid_and_when_and_a_member_does_not()
    {
        var (_, boliId) = await OpenBoliAsync("Paryushan");
        var (early, late) = (Guid.NewGuid(), Guid.NewGuid());

        await Member(early).PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await Member(late).PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 2_000_00L });

        var ledger = (await Manager().GetFromJsonAsync<List<BidLedgerEntry>>($"/v1/boli/boli/{boliId}/bids/ledger"))!;

        ledger.Select(b => (b.MemberId, b.Amount)).Should().Equal(
            [(late, 2_000_00L), (early, 1_000_00L)], "highest first, each with its bidder");
        ledger[0].PlacedAt.Should().BeAfter(ledger[1].PlacedAt);

        (await Member(early).GetAsync($"/v1/boli/boli/{boliId}/bids/ledger"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "who is prepared to pay what is not for members");

        // The members' history is unchanged: amounts, times, and only "is this mine".
        var history = await Member(early).GetStringAsync($"/v1/boli/boli/{boliId}/bids");
        history.Should().NotContain(late.ToString());
    }

    [Fact]
    public async Task An_occasion_keeps_its_venue_and_city_and_every_Boli_carries_them()
    {
        var (locality, city) = (Guid.NewGuid(), Guid.NewGuid());
        var (occasionId, boliId) = await OpenBoliAsync("Hiran Magri Paryushan", locality, city, "Shri Mahavir Mandir");

        var detail = await Member(Guid.NewGuid()).GetFromJsonAsync<OccasionDetailResponse>($"/v1/boli/occasions/{occasionId}");
        detail!.Venue.Should().Be("Shri Mahavir Mandir");
        detail.CityId.Should().Be(city);

        var lot = await Member(Guid.NewGuid()).GetFromJsonAsync<BoliResponse>($"/v1/boli/boli/{boliId}");
        lot!.OccasionTitle.Should().Be("Hiran Magri Paryushan");
        lot.LocalityId.Should().Be(locality);
        lot.CityId.Should().Be(city);
        lot.Venue.Should().Be("Shri Mahavir Mandir");

        (await Manager().PatchAsJsonAsync($"/v1/boli/occasions/{occasionId}", new
            {
                title = "Hiran Magri Paryushan",
                description = (string?)null,
                occasionDate = "2026-09-10",
                localityId = locality,
                venue = "  ",
                cityId = (Guid?)null,
            }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var corrected = await Member(Guid.NewGuid()).GetFromJsonAsync<OccasionDetailResponse>($"/v1/boli/occasions/{occasionId}");
        corrected!.Venue.Should().BeNull("blank means none");
        corrected.CityId.Should().BeNull();
    }

    [Fact]
    public async Task Occasions_and_open_Boli_can_be_narrowed_to_a_city_or_village()
    {
        var (cityA, cityB) = (Guid.NewGuid(), Guid.NewGuid());
        var (occasionA, boliA) = await OpenBoliAsync("In A", cityId: cityA);
        var (occasionB, boliB) = await OpenBoliAsync("In B", cityId: cityB);

        var occasions = await Member(Guid.NewGuid())
            .GetFromJsonAsync<List<OccasionResponse>>($"/v1/boli/occasions?cityId={cityA}");
        occasions!.Select(o => o.Id).Should().Equal([occasionA]);

        var open = await Member(Guid.NewGuid()).GetFromJsonAsync<List<BoliResponse>>($"/v1/boli/boli/active?cityId={cityA}");
        open!.Select(b => b.Id).Should().Equal([boliA]);

        (await Member(Guid.NewGuid()).GetFromJsonAsync<List<BoliResponse>>("/v1/boli/boli/active"))!
            .Select(b => b.Id).Should().BeEquivalentTo([boliA, boliB]);
        occasionB.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_published_result_names_where_its_occasion_is()
    {
        var (locality, city) = (Guid.NewGuid(), Guid.NewGuid());
        var (occasionId, boliId) = await OpenBoliAsync("Results", locality, city);
        var winner = Guid.NewGuid();
        var manager = Manager();

        await Member(winner).PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 3_000_00L });
        await manager.PostAsync($"/v1/boli/boli/{boliId}/close", null);
        await manager.PostAsync($"/v1/boli/boli/{boliId}/result", null);
        await manager.PostAsync($"/v1/boli/boli/{boliId}/result/publish", null);

        var results = await Member(Guid.NewGuid()).GetFromJsonAsync<List<BoliResultResponse>>("/v1/boli/results");
        var announced = results!.Single(r => r.BoliId == boliId);

        announced.WinningMemberId.Should().Be(winner, "published, so every member may see who won");
        announced.OccasionId.Should().Be(occasionId);
        announced.LocalityId.Should().Be(locality);
        announced.CityId.Should().Be(city);
    }
}
