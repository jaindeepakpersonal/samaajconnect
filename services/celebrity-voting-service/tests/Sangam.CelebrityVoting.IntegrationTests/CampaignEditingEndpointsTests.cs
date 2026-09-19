using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.CelebrityVoting.Domain.Campaigns;
using Xunit;

namespace Sangam.CelebrityVoting.IntegrationTests;

/// <summary>
/// Editing a campaign through the endpoint, against a real database.
/// </summary>
public sealed class CampaignEditingEndpointsTests(CelebrityVotingApiFactory factory)
    : IClassFixture<CelebrityVotingApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantId, ["SamaajAdmin"], ["CelebrityVoting.Configure", "Members.Read"]);

    private HttpClient Member() =>
        factory.CreateClientAs(Guid.NewGuid(), TenantId, ["Member"], ["Members.Read"]);

    private static object Body(string title = "Celebrities 2026", int topN = 3, string visibility = "Live",
        int votingStartOffsetDays = 7) => new
        {
            title,
            description = "About it",
            nominationStartAt = DateTimeOffset.UtcNow,
            nominationEndAt = DateTimeOffset.UtcNow.AddDays(7),
            votingStartAt = DateTimeOffset.UtcNow.AddDays(votingStartOffsetDays),
            votingEndAt = DateTimeOffset.UtcNow.AddDays(14),
            topN,
            resultsVisibility = visibility,
        };

    private async Task<Guid> CreateAsync()
    {
        var response = await Admin().PostAsJsonAsync("/v1/celebrity-voting/campaigns", Body());
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task An_admin_corrects_a_draft_campaign()
    {
        var id = await CreateAsync();

        var response = await Admin().PatchAsJsonAsync(
            $"/v1/celebrity-voting/campaigns/{id}",
            Body(title: "Renamed", topN: 5, visibility: "HiddenUntilClose"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("title").GetString().Should().Be("Renamed");
        body.GetProperty("topN").GetInt32().Should().Be(5);
        body.GetProperty("resultsVisibility").GetString().Should().Be("HiddenUntilClose");
    }

    [Fact]
    public async Task An_edit_writes_an_outbox_row_in_the_same_transaction()
    {
        var id = await CreateAsync();

        await Admin().PatchAsJsonAsync($"/v1/celebrity-voting/campaigns/{id}", Body(title: "Renamed"));

        (await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AnyAsync(m => m.Topic == "celebrity-voting.campaign.updated.v1")))
            .Should().BeTrue();
    }

    [Fact]
    public async Task An_edit_that_breaks_the_window_rules_is_refused_like_a_create()
    {
        var id = await CreateAsync();

        // Voting would start before nominations close.
        var response = await Admin().PatchAsJsonAsync(
            $"/v1/celebrity-voting/campaigns/{id}", Body(votingStartOffsetDays: 3));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_cannot_edit_a_campaign()
    {
        var id = await CreateAsync();

        (await Member().PatchAsJsonAsync($"/v1/celebrity-voting/campaigns/{id}", Body()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_Samaajs_campaign_is_not_found()
    {
        var id = await CreateAsync();

        (await Admin(OtherTenantId).PatchAsJsonAsync($"/v1/celebrity-voting/campaigns/{id}", Body()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Editing_is_refused_once_voting_has_opened()
    {
        var id = await CreateAsync();

        await factory.WithDbContextAsync(async db =>
        {
            var campaign = await db.Campaigns.IgnoreQueryFilters().Include(c => c.Candidates).SingleAsync(c => c.Id == id);
            var now = campaign.NominationStartAt.AddHours(1);

            campaign.MoveTo(CampaignStatus.NominationsOpen, now);
            var candidate = campaign.Nominate(Guid.NewGuid(), "Education", Guid.NewGuid(), now)!;
            campaign.ApproveCandidate(candidate.Id, Guid.NewGuid(), now);
            campaign.MoveTo(CampaignStatus.VotingOpen, now).Should().BeTrue();

            await db.SaveChangesAsync();
            return 0;
        });

        var response = await Admin().PatchAsJsonAsync(
            $"/v1/celebrity-voting/campaigns/{id}", Body(title: "Too late"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
