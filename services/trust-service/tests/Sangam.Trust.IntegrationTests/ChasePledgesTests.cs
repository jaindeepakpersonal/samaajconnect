using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.Trust.Application.IntegrationEvents;
using Xunit;

namespace Sangam.Trust.IntegrationTests;

/// <summary>
/// A pledge is chased like any other amount due: it opens a Collection, the
/// donor sees it, an officer can remind them, and settling either the donation
/// or its Collection settles both.
/// </summary>
public sealed class ChasePledgesTests(TrustApiFactory factory)
    : IClassFixture<TrustApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync()
    {
        factory.Clock.Set(Start);
        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin() =>
        factory.CreateClientAs(Guid.NewGuid(), TenantId, ["SamaajAdmin"], ["Trust.Manage", "Members.Read"]);

    private HttpClient Member(Guid memberId) =>
        factory.CreateClientAs(memberId, TenantId, ["Member"], ["Members.Read"]);

    private async Task<Guid> CreateTrustAsync()
    {
        var response = await Admin().PostAsJsonAsync(
            "/v1/trust/trusts", new { localityId = Guid.NewGuid().ToString(), name = "Hiran Magri Trust" });

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> PledgeAsync(Guid trustId, Guid? donorMemberId, string? donorName = null)
    {
        var response = await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/donations", new
        {
            donorMemberId = donorMemberId?.ToString(),
            donorName,
            donorPhone = (string?)null,
            amount = 1_100_000L,
            purpose = "Temple fund",
            mode = "Upi",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> CollectionForAsync(Guid trustId, Guid donationId)
    {
        var all = await Admin().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/collections");

        return all.EnumerateArray().Single(c => c.GetProperty("sourceId").GetGuid() == donationId);
    }

    [Fact]
    public async Task A_members_pledge_is_owed_by_them_until_it_is_paid_and_paying_it_issues_the_receipt()
    {
        var trustId = await CreateTrustAsync();
        var donor = Guid.NewGuid();
        var donationId = await PledgeAsync(trustId, donor);

        var mine = await Member(donor).GetFromJsonAsync<JsonElement>("/v1/trust/collections/mine");

        mine.GetArrayLength().Should().Be(1);
        mine[0].GetProperty("sourceType").GetString().Should().Be("Donation");
        mine[0].GetProperty("amount").GetInt64().Should().Be(1_100_000L);

        var owed = await CollectionForAsync(trustId, donationId);
        owed.GetProperty("payerMemberId").GetGuid().Should().Be(donor);

        (await Admin().PostAsJsonAsync($"/v1/trust/collections/{owed.GetProperty("id").GetGuid()}/payment", new { mode = "Cash" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var donations = await Admin().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/donations");
        var donation = donations.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == donationId);

        donation.GetProperty("status").GetString().Should().Be("Collected", "paying the pledge is collecting it");
        donation.GetProperty("receiptNumber").GetInt32().Should().Be(1);

        (await Member(donor).GetFromJsonAsync<JsonElement>("/v1/trust/collections/mine")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Collecting_the_donation_settles_what_is_owed_for_it()
    {
        var trustId = await CreateTrustAsync();
        var donor = Guid.NewGuid();
        var donationId = await PledgeAsync(trustId, donor);

        (await Admin().PostAsync($"/v1/trust/donations/{donationId}/collect", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var owed = await CollectionForAsync(trustId, donationId);

        owed.GetProperty("status").GetString().Should().Be("Collected");
        owed.GetProperty("mode").GetString().Should().Be("Upi", "settled by the mode it was pledged by");
        (await Member(donor).GetFromJsonAsync<JsonElement>("/v1/trust/collections/mine")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_donor_with_no_account_is_named_and_cannot_be_sent_a_reminder()
    {
        var trustId = await CreateTrustAsync();
        var donationId = await PledgeAsync(trustId, donorMemberId: null, donorName: "Shri Mahendra Jain");

        var owed = await CollectionForAsync(trustId, donationId);

        owed.GetProperty("payerName").GetString().Should().Be("Shri Mahendra Jain");
        owed.GetProperty("payerMemberId").ValueKind.Should().Be(JsonValueKind.Null);

        (await Admin().PostAsync($"/v1/trust/collections/{owed.GetProperty("id").GetGuid()}/remind", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "there is no inbox to put it in");
    }

    [Fact]
    public async Task A_reminder_goes_out_once_a_day_and_never_once_it_is_paid()
    {
        var trustId = await CreateTrustAsync();
        var donor = Guid.NewGuid();
        var donationId = await PledgeAsync(trustId, donor);
        var collectionId = (await CollectionForAsync(trustId, donationId)).GetProperty("id").GetGuid();
        var admin = Admin();

        var first = await admin.PostAsync($"/v1/trust/collections/{collectionId}/remind", null);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lastRemindedAt").GetDateTimeOffset()
            .Should().Be(Start);

        (await admin.PostAsync($"/v1/trust/collections/{collectionId}/remind", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "a second press the same day must not send a second one");

        factory.Clock.Advance(TimeSpan.FromHours(25));

        (await admin.PostAsync($"/v1/trust/collections/{collectionId}/remind", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var reminders = await factory.WithDbContextAsync(db => db.OutboxMessages
            .Where(m => m.Topic == "trust.collection.reminded.v1")
            .Select(m => m.Payload)
            .ToListAsync());

        reminders.Should().HaveCount(2);
        reminders[0].Should().Contain(donor.ToString()).And.NotContain("1100000", "the event names who, not how much");

        await admin.PostAsJsonAsync($"/v1/trust/collections/{collectionId}/payment", new { mode = "Cash" });
        factory.Clock.Advance(TimeSpan.FromHours(25));

        (await admin.PostAsync($"/v1/trust/collections/{collectionId}/remind", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "nobody is chased for something paid");
    }

    [Fact]
    public async Task A_Boli_winner_can_be_reminded_too()
    {
        var localityId = Guid.NewGuid();
        var response = await Admin().PostAsJsonAsync(
            "/v1/trust/trusts", new { localityId = localityId.ToString(), name = "Surajpole Trust" });
        var trustId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var winner = Guid.NewGuid();
        var boliId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ConsumeIntegrationEventCommand(
                new IntegrationEventEnvelope(
                    Guid.NewGuid(),
                    TenantId,
                    ConsumeIntegrationEventCommandHandler.BoliResultPublishedTopic,
                    "BoliResultPublished",
                    JsonSerializer.Serialize(new { boliId, winningMemberId = winner, amount = 500_000L, localityId }),
                    Start)));
        }

        var owed = await CollectionForAsync(trustId, boliId);

        (await Admin().PostAsync($"/v1/trust/collections/{owed.GetProperty("id").GetGuid()}/remind", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_member_cannot_send_reminders()
    {
        var trustId = await CreateTrustAsync();
        var donationId = await PledgeAsync(trustId, Guid.NewGuid());
        var collectionId = (await CollectionForAsync(trustId, donationId)).GetProperty("id").GetGuid();

        (await Member(Guid.NewGuid()).PostAsync($"/v1/trust/collections/{collectionId}/remind", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
