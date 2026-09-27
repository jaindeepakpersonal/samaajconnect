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

/// <summary>Donations, expenses and collections, through the endpoints, against a real database.</summary>
public sealed class TrustFinanceEndpointsTests(TrustApiFactory factory)
    : IClassFixture<TrustApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantId, ["SamaajAdmin"], ["Trust.Manage", "Members.Read"]);

    private HttpClient Member(Guid memberId) =>
        factory.CreateClientAs(memberId, TenantId, ["Member"], ["Members.Read"]);

    private async Task<(Guid TrustId, Guid LocalityId)> CreateTrustAsync()
    {
        var localityId = Guid.NewGuid();
        var response = await Admin().PostAsJsonAsync(
            "/v1/trust/trusts", new { localityId = localityId.ToString(), name = "Hiran Magri Trust" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        return (id, localityId);
    }

    private static object Pledge(string? donorName = "Shri Mahendra Jain", string? phone = null) => new
    {
        donorMemberId = (string?)null,
        donorName,
        donorPhone = phone,
        amount = 510_000L,
        purpose = "Temple fund",
        mode = "Cash",
    };

    // ---- Donations -----------------------------------------------------------

    [Fact]
    public async Task A_pledge_gets_its_receipt_number_when_it_is_collected_and_only_once()
    {
        var (trustId, _) = await CreateTrustAsync();
        var admin = Admin();

        var recorded = await admin.PostAsJsonAsync($"/v1/trust/trusts/{trustId}/donations", Pledge());

        recorded.StatusCode.Should().Be(HttpStatusCode.Created);

        var pledge = await recorded.Content.ReadFromJsonAsync<JsonElement>();
        pledge.GetProperty("status").GetString().Should().Be("Pledged");
        pledge.GetProperty("receiptNumber").ValueKind.Should().Be(JsonValueKind.Null);

        var donationId = pledge.GetProperty("id").GetGuid();

        var collected = await (await admin.PostAsync($"/v1/trust/donations/{donationId}/collect", null))
            .Content.ReadFromJsonAsync<JsonElement>();

        collected.GetProperty("status").GetString().Should().Be("Collected");
        collected.GetProperty("receiptNumber").GetInt32().Should().Be(1);

        (await admin.PostAsync($"/v1/trust/donations/{donationId}/collect", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "a second receipt for one gift would be two receipts");

        var pledged = await admin.GetFromJsonAsync<JsonElement>(
            $"/v1/trust/trusts/{trustId}/donations?status=Pledged");

        pledged.GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData(null, null)]              // nobody named at all
    [InlineData("Shri Jain", "call me")]  // a phone that is not one
    public async Task A_donation_needs_a_donor_and_a_phone_that_is_a_phone(string? donorName, string? phone)
    {
        var (trustId, _) = await CreateTrustAsync();

        (await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/donations", Pledge(donorName, phone)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_cannot_read_a_Trusts_books()
    {
        var (trustId, _) = await CreateTrustAsync();
        var member = Member(Guid.NewGuid());

        (await member.GetAsync($"/v1/trust/trusts/{trustId}/donations")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"/v1/trust/trusts/{trustId}/expenses")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await member.GetAsync($"/v1/trust/trusts/{trustId}/collections")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_Samaaj_cannot_collect_this_Trusts_pledge()
    {
        var (trustId, _) = await CreateTrustAsync();

        var pledge = await (await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/donations", Pledge()))
            .Content.ReadFromJsonAsync<JsonElement>();

        (await Admin(OtherTenantId).PostAsync($"/v1/trust/donations/{pledge.GetProperty("id").GetGuid()}/collect", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Expenses ------------------------------------------------------------

    [Fact]
    public async Task An_expense_is_recorded_then_approved_and_approving_again_changes_nothing()
    {
        var (trustId, _) = await CreateTrustAsync();
        var admin = Admin();

        var recorded = await admin.PostAsJsonAsync($"/v1/trust/trusts/{trustId}/expenses", new
        {
            category = "Maintenance",
            description = "Roof repair",
            amount = 1_250_000L,
            vendor = "Mewar Builders",
        });

        recorded.StatusCode.Should().Be(HttpStatusCode.Created);

        var expense = await recorded.Content.ReadFromJsonAsync<JsonElement>();
        expense.GetProperty("status").GetString().Should().Be("Recorded");

        var id = expense.GetProperty("id").GetGuid();

        var approved = await (await admin.PostAsync($"/v1/trust/expenses/{id}/approve", null))
            .Content.ReadFromJsonAsync<JsonElement>();
        var again = await admin.PostAsync($"/v1/trust/expenses/{id}/approve", null);

        approved.GetProperty("status").GetString().Should().Be("Approved");
        again.StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/expenses?status=Recorded"))
            .GetArrayLength().Should().Be(0);
    }

    // ---- Collections ---------------------------------------------------------

    [Fact]
    public async Task A_won_Boli_is_owed_by_its_winner_until_the_Trust_records_the_payment()
    {
        var (trustId, localityId) = await CreateTrustAsync();
        var winner = Guid.NewGuid();
        var boliId = Guid.NewGuid();

        await PublishBoliResultAsync(boliId, winner, 2_100_000L, localityId);

        var mine = await Member(winner).GetFromJsonAsync<JsonElement>("/v1/trust/collections/mine");

        mine.GetArrayLength().Should().Be(1);
        mine[0].GetProperty("amount").GetInt64().Should().Be(2_100_000L);
        mine[0].GetProperty("trustId").GetGuid().Should().Be(trustId);

        (await Member(Guid.NewGuid()).GetFromJsonAsync<JsonElement>("/v1/trust/collections/mine"))
            .GetArrayLength().Should().Be(0, "somebody else's debt is not yours to see");

        var admin = Admin();
        var due = await admin.GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/collections?status=Due");

        due.GetArrayLength().Should().Be(1);
        due[0].GetProperty("payerMemberId").GetGuid().Should().Be(winner);
        due[0].GetProperty("sourceType").GetString().Should().Be("BoliResult");
        due[0].GetProperty("sourceId").GetGuid().Should().Be(boliId);

        var collectionId = due[0].GetProperty("id").GetGuid();

        var paid = await admin.PostAsJsonAsync($"/v1/trust/collections/{collectionId}/payment", new { mode = "UPI" });

        paid.StatusCode.Should().Be(HttpStatusCode.OK);
        (await paid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().Should().Be("Collected");

        (await admin.PostAsJsonAsync($"/v1/trust/collections/{collectionId}/payment", new { mode = "UPI" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await Member(winner).GetFromJsonAsync<JsonElement>("/v1/trust/collections/mine"))
            .GetArrayLength().Should().Be(0, "a paid amount is no longer owed");
    }

    [Fact]
    public async Task A_payment_needs_a_mode()
    {
        var (_, localityId) = await CreateTrustAsync();
        var boliId = Guid.NewGuid();

        await PublishBoliResultAsync(boliId, Guid.NewGuid(), 100_000L, localityId);

        var collectionId = await factory.WithDbContextAsync(db => db.Collections
            .IgnoreQueryFilters()
            .Where(c => c.SourceId == boliId)
            .Select(c => c.Id)
            .SingleAsync());

        (await Admin().PostAsJsonAsync($"/v1/trust/collections/{collectionId}/payment", new { mode = "" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task PublishBoliResultAsync(Guid boliId, Guid winner, long amount, Guid localityId)
    {
        var envelope = new IntegrationEventEnvelope(
            Guid.NewGuid(),
            TenantId,
            ConsumeIntegrationEventCommandHandler.BoliResultPublishedTopic,
            "BoliResultPublished",
            JsonSerializer.Serialize(new { boliId, winningMemberId = winner, amount, localityId }),
            DateTimeOffset.UtcNow);

        await using var scope = factory.Services.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ConsumeIntegrationEventCommand(envelope));

        result.Value.Should().Be(1);
    }
}

