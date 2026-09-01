using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Boli.Application.Auctions;
using Sangam.Boli.Application.Security;
using Xunit;

namespace Sangam.Boli.IntegrationTests;

/// <summary>
/// A Boli from announced to announced: occasion, type, open, bid, close,
/// record, publish.
/// </summary>
public sealed class BoliLifecycleTests(BoliApiFactory factory)
    : IClassFixture<BoliApiFactory>, IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Manager() => factory.CreateClientAs(
        ManagerId,
        Tenant,
        [Roles.BoliManager],
        [PermissionKeys.BoliManage, PermissionKeys.BoliPublishResults, PermissionKeys.MembersRead]);

    /// <summary>A manager who may run a Boli but not announce its result.</summary>
    private HttpClient ManagerWhoCannotPublish() => factory.CreateClientAs(
        ManagerId, Tenant, [Roles.BoliManager], [PermissionKeys.BoliManage, PermissionKeys.MembersRead]);

    private HttpClient Member(Guid memberId) => factory.CreateClientAs(
        memberId, Tenant, [Roles.Member], [PermissionKeys.MembersRead]);

    private async Task<Guid> OpenBoliAsync()
    {
        var manager = Manager();
        var now = factory.Clock.UtcNow;

        var occasion = await (await manager.PostAsJsonAsync("/v1/boli/occasions", new
        {
            title = "Paryushan 2026",
            description = (string?)null,
            occasionDate = "2026-09-10",
        })).Content.ReadFromJsonAsync<OccasionResponse>();

        var type = await (await manager.PostAsJsonAsync(
            $"/v1/boli/occasions/{occasion!.Id}/boli-types",
            new { name = "Mangal Deep", description = (string?)null }))
            .Content.ReadFromJsonAsync<BoliTypeResponse>();

        var lot = await (await manager.PostAsJsonAsync($"/v1/boli/occasions/{occasion.Id}/boli", new
        {
            boliTypeId = type!.Id,
            title = "Mangal Deep",
            startAt = now.AddMinutes(-5),
            endAt = now.AddHours(2),
            startingAmount = 1_000_00L,
            minIncrement = 500_00L,
            eligibilityRule = "One per family.",
        })).Content.ReadFromJsonAsync<BoliResponse>();

        return lot!.Id;
    }

    [Fact]
    public async Task A_Boli_runs_from_open_to_published()
    {
        var boliId = await OpenBoliAsync();
        var winner = Guid.NewGuid();
        var manager = Manager();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });
        await Member(winner)
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 5_000_00L });

        (await manager.PostAsync($"/v1/boli/boli/{boliId}/close", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var recorded = await manager.PostAsync($"/v1/boli/boli/{boliId}/result", null);

        recorded.StatusCode.Should().Be(HttpStatusCode.OK);

        var recordedBody = await recorded.Content.ReadFromJsonAsync<BoliResultResponse>();

        recordedBody!.Amount.Should().Be(5_000_00L);
        recordedBody.IsPublished.Should().BeFalse();

        // Recorded is not announced. Nobody is named yet, not even to the manager
        // who recorded it - a shape that carries the winner "but only to the
        // right caller" is one authorization mistake away from announcing early.
        recordedBody.WinningMemberId.Should().BeNull();

        var published = await manager.PostAsync($"/v1/boli/boli/{boliId}/result/publish", null);

        published.StatusCode.Should().Be(HttpStatusCode.OK);

        var publishedBody = await published.Content.ReadFromJsonAsync<BoliResultResponse>();

        publishedBody!.IsPublished.Should().BeTrue();
        publishedBody.WinningMemberId.Should().Be(winner);
    }

    [Fact]
    public async Task The_winner_comes_from_the_highest_bid_and_is_not_a_parameter()
    {
        // Taking a winner as input would let a recorded result name somebody who
        // never made the highest bid, with the append-only bid history sitting
        // beside it contradicting it.
        var boliId = await OpenBoliAsync();
        var highest = Guid.NewGuid();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 2_000_00L });
        await Member(highest)
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 9_000_00L });
        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 3_000_00L });

        var manager = Manager();

        await manager.PostAsync($"/v1/boli/boli/{boliId}/close", null);
        await manager.PostAsync($"/v1/boli/boli/{boliId}/result", null);

        var published = await (await manager.PostAsync($"/v1/boli/boli/{boliId}/result/publish", null))
            .Content.ReadFromJsonAsync<BoliResultResponse>();

        published!.WinningMemberId.Should().Be(highest);
        published.Amount.Should().Be(9_000_00L);
    }

    [Fact]
    public async Task A_result_cannot_be_recorded_before_the_bidding_closes()
    {
        var boliId = await OpenBoliAsync();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });

        var response = await Manager().PostAsync($"/v1/boli/boli/{boliId}/result", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Publishing_twice_is_safe_and_keeps_the_first_announcement()
    {
        var boliId = await OpenBoliAsync();
        var manager = Manager();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });

        await manager.PostAsync($"/v1/boli/boli/{boliId}/close", null);
        await manager.PostAsync($"/v1/boli/boli/{boliId}/result", null);

        var first = await (await manager.PostAsync($"/v1/boli/boli/{boliId}/result/publish", null))
            .Content.ReadFromJsonAsync<BoliResultResponse>();

        factory.Clock.Advance(TimeSpan.FromHours(1));

        var second = await manager.PostAsync($"/v1/boli/boli/{boliId}/result/publish", null);

        second.StatusCode.Should().Be(HttpStatusCode.OK, "a retried request has to be safe");

        var secondBody = await second.Content.ReadFromJsonAsync<BoliResultResponse>();

        secondBody!.PublishedAt.Should().Be(first!.PublishedAt);

        factory.Clock.Advance(TimeSpan.FromHours(-1));
    }

    [Fact]
    public async Task Announcing_needs_its_own_permission()
    {
        var boliId = await OpenBoliAsync();
        var manager = Manager();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });

        await manager.PostAsync($"/v1/boli/boli/{boliId}/close", null);
        await manager.PostAsync($"/v1/boli/boli/{boliId}/result", null);

        // Boli.Manage runs the Boli; Boli.PublishResults is the step that cannot
        // be taken back.
        var response = await ManagerWhoCannotPublish()
            .PostAsync($"/v1/boli/boli/{boliId}/result/publish", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Closing_a_Boli_writes_an_outbox_row_in_the_same_transaction()
    {
        var boliId = await OpenBoliAsync();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });

        await Manager().PostAsync($"/v1/boli/boli/{boliId}/close", null);

        var topics = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.IgnoreQueryFilters().Select(m => m.Topic).ToListAsync());

        topics.Should().Contain("boli.closed.v1");
    }

    [Fact]
    public async Task A_member_cannot_run_a_Boli()
    {
        var response = await Member(Guid.NewGuid()).PostAsJsonAsync("/v1/boli/occasions", new
        {
            title = "Not mine to announce",
            description = (string?)null,
            occasionDate = "2026-09-10",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Boli_from_another_Samaaj_is_not_found_rather_than_forbidden()
    {
        var boliId = await OpenBoliAsync();

        var outsider = factory.CreateClientAs(
            Guid.NewGuid(), Guid.NewGuid(), [Roles.Member], [PermissionKeys.MembersRead]);

        var response = await outsider.PostAsJsonAsync(
            $"/v1/boli/boli/{boliId}/bids", new { amount = 9_999_00L });

        // Not 403: a 403 would confirm that this Boli id is real.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
