using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Boli.Application.Auctions;
using Sangam.Boli.Application.Auctions.Queries;
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

    // ---- The publisher's queue ---------------------------------------------

    [Fact]
    public async Task A_recorded_result_waits_in_the_publication_queue()
    {
        // The middle state of the platform's most deliberate two-step workflow,
        // which nothing could list until now. A result that cannot be found is
        // a result that is announced only if somebody remembers it.
        var boliId = await OpenBoliAsync();
        var manager = Manager();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 5_000_00L });
        await manager.PostAsync($"/v1/boli/boli/{boliId}/close", null);

        (await manager.GetFromJsonAsync<List<PendingResultResponse>>("/v1/boli/results/pending"))
            .Should().BeEmpty("nothing is waiting until a result has been recorded");

        await manager.PostAsync($"/v1/boli/boli/{boliId}/result", null);

        var pending = await manager
            .GetFromJsonAsync<List<PendingResultResponse>>("/v1/boli/results/pending");

        pending.Should().ContainSingle();
        pending![0].BoliId.Should().Be(boliId);
        pending[0].BoliTitle.Should().Be("Mangal Deep");
        pending[0].Amount.Should().Be(5_000_00L);
        pending[0].RecordedBy.Should().Be(ManagerId);

        await manager.PostAsync($"/v1/boli/boli/{boliId}/result/publish", null);

        (await manager.GetFromJsonAsync<List<PendingResultResponse>>("/v1/boli/results/pending"))
            .Should().BeEmpty("announcing it takes it out of the queue");
    }

    [Fact]
    public async Task The_queue_names_the_amount_and_not_the_winner()
    {
        // The wireframe's publish screen draws "Winning Bid: Rs 18,400 - Member
        // ID 1042". The winner is not here, on purpose: one record names the
        // winner and only after publication, which is a far easier invariant to
        // keep than two records naming them, one of which only to the right
        // caller. Nothing is lost - the winner is read from the highest bid and
        // is not something the publisher chooses.
        var boliId = await OpenBoliAsync();
        var winner = Guid.NewGuid();
        var manager = Manager();

        await Member(winner)
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 7_500_00L });
        await manager.PostAsync($"/v1/boli/boli/{boliId}/close", null);
        await manager.PostAsync($"/v1/boli/boli/{boliId}/result", null);

        var body = await manager.GetStringAsync("/v1/boli/results/pending");

        body.Should().Contain("750000");
        body.Should().NotContain(winner.ToString());
    }

    [Fact]
    public async Task The_queue_belongs_to_whoever_may_publish()
    {
        // Boli.PublishResults, not Boli.Manage. The two currently separate
        // nobody, but a Samaaj that wants a second pair of eyes on
        // announcements should get a queue that belongs to the eyes.
        var boliId = await OpenBoliAsync();
        var manager = Manager();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 5_000_00L });
        await manager.PostAsync($"/v1/boli/boli/{boliId}/close", null);
        await manager.PostAsync($"/v1/boli/boli/{boliId}/result", null);

        (await ManagerWhoCannotPublish().GetAsync("/v1/boli/results/pending"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Member(Guid.NewGuid()).GetAsync("/v1/boli/results/pending"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_queue_puts_the_longest_waiting_result_first()
    {
        // Oldest first, unlike the published list. This is a work queue, and the
        // one waiting longest is the one most likely to have been forgotten.
        var manager = Manager();
        var first = await OpenBoliAsync();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{first}/bids", new { amount = 5_000_00L });
        await manager.PostAsync($"/v1/boli/boli/{first}/close", null);
        await manager.PostAsync($"/v1/boli/boli/{first}/result", null);

        factory.Clock.Advance(TimeSpan.FromHours(1));

        var second = await OpenBoliAsync();

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{second}/bids", new { amount = 9_000_00L });
        await manager.PostAsync($"/v1/boli/boli/{second}/close", null);
        await manager.PostAsync($"/v1/boli/boli/{second}/result", null);

        var pending = await manager
            .GetFromJsonAsync<List<PendingResultResponse>>("/v1/boli/results/pending");

        pending.Should().HaveCount(2);
        pending![0].BoliId.Should().Be(first);
    }

    // ---- Anti-sniping, through a real bid on a real database ----------------

    [Fact]
    public async Task A_bid_in_the_closing_seconds_moves_the_close_out()
    {
        // The unit tests prove the rule; this proves it survives the round trip
        // through the handler, the row lock and the column - the extension
        // happens inside the same transaction as the bid, so a Boli that
        // extended but did not save the bid, or the reverse, would show up here.
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
            new { name = "Swapna", description = (string?)null }))
            .Content.ReadFromJsonAsync<BoliTypeResponse>();

        // Closing in thirty seconds, with a two-minute window: a bid now is
        // inside it.
        var closingSoon = now.AddSeconds(30);

        var lot = await (await manager.PostAsJsonAsync($"/v1/boli/occasions/{occasion.Id}/boli", new
        {
            boliTypeId = type!.Id,
            title = "Swapna",
            startAt = now.AddMinutes(-5),
            endAt = closingSoon,
            startingAmount = 1_000_00L,
            minIncrement = 500_00L,
            eligibilityRule = (string?)null,
            autoExtendSeconds = 120,
        })).Content.ReadFromJsonAsync<BoliResponse>();

        lot!.AutoExtendSeconds.Should().Be(120);
        lot.EndAt.Should().Be(closingSoon);

        var placed = await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{lot.Id}/bids", new { amount = 1_000_00L });

        placed.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await Manager().GetFromJsonAsync<BoliResponse>($"/v1/boli/boli/{lot.Id}");

        // Measured from the bid, not from the old close.
        after!.EndAt.Should().Be(now.AddSeconds(120));
        after.EndAt.Should().BeAfter(closingSoon);

        // And it is genuinely still open at the time it would have shut.
        after.AcceptsBids.Should().BeTrue();
    }

    [Fact]
    public async Task A_bid_nowhere_near_the_close_leaves_it_alone()
    {
        // Almost every bid. `OpenBoliAsync` closes in two hours and asks for no
        // window at all, so this also covers the default: a Boli that never
        // mentioned auto-extend behaves exactly as it did before the column
        // existed.
        var boliId = await OpenBoliAsync();

        var before = await Manager().GetFromJsonAsync<BoliResponse>($"/v1/boli/boli/{boliId}");

        before!.AutoExtendSeconds.Should().Be(0);

        await Member(Guid.NewGuid())
            .PostAsJsonAsync($"/v1/boli/boli/{boliId}/bids", new { amount = 1_000_00L });

        var after = await Manager().GetFromJsonAsync<BoliResponse>($"/v1/boli/boli/{boliId}");

        after!.EndAt.Should().Be(before.EndAt);
    }

    [Fact]
    public async Task An_auto_extend_window_longer_than_an_hour_is_refused()
    {
        // A window that long extends on essentially every bid, which is not
        // anti-sniping - it is an auction that never ends.
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
            new { name = "Aarti", description = (string?)null }))
            .Content.ReadFromJsonAsync<BoliTypeResponse>();

        var refused = await manager.PostAsJsonAsync($"/v1/boli/occasions/{occasion.Id}/boli", new
        {
            boliTypeId = type!.Id,
            title = "Aarti",
            startAt = now,
            endAt = now.AddHours(2),
            startingAmount = 1_000_00L,
            minIncrement = 500_00L,
            eligibilityRule = (string?)null,
            autoExtendSeconds = 7200,
        });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
