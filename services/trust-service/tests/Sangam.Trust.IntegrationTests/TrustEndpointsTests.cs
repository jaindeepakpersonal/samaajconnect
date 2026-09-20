using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sangam.Trust.IntegrationTests;

/// <summary>Trusts and their rosters, through the endpoints, against a real database.</summary>
public sealed class TrustEndpointsTests(TrustApiFactory factory)
    : IClassFixture<TrustApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync()
    {
        factory.Clock.Set(Start);
        return factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantId, ["SamaajAdmin"], ["Trust.Manage", "Members.Read"]);

    private HttpClient Member(Guid? tenantId = null) =>
        factory.CreateClientAs(Guid.NewGuid(), tenantId ?? TenantId, ["Member"], ["Members.Read"]);

    private async Task<Guid> CreateTrustAsync(Guid? localityId = null, string name = "Hiran Magri Trust")
    {
        var response = await Admin().PostAsJsonAsync(
            "/v1/trust/trusts", new { localityId = (localityId ?? Guid.NewGuid()).ToString(), name });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_member_cannot_create_a_trust()
    {
        (await Member().PostAsJsonAsync("/v1/trust/trusts", new { localityId = Guid.NewGuid().ToString(), name = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Creating_a_trust_makes_it_listable_and_gettable()
    {
        var localityId = Guid.NewGuid();
        var trustId = await CreateTrustAsync(localityId, "Surajpole Trust");

        var got = await Member().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}");
        got.GetProperty("name").GetString().Should().Be("Surajpole Trust");
        got.GetProperty("status").GetString().Should().Be("Active");

        var listed = await Member().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts?localityId={localityId}");
        listed.EnumerateArray().Should().ContainSingle(t => t.GetProperty("id").GetGuid() == trustId);
    }

    [Fact]
    public async Task A_second_trust_for_the_same_locality_is_refused()
    {
        var localityId = Guid.NewGuid();
        await CreateTrustAsync(localityId);

        (await Admin().PostAsJsonAsync(
                "/v1/trust/trusts", new { localityId = localityId.ToString(), name = "Second Trust" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Deactivating_a_trust_twice_is_success_the_second_time_too()
    {
        var trustId = await CreateTrustAsync();

        (await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/status", new { status = "Inactive" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/status", new { status = "Inactive" });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()
            .Should().Be("Inactive");
    }

    [Fact]
    public async Task Adding_updating_and_removing_a_member_walks_the_roster()
    {
        var trustId = await CreateTrustAsync();
        var memberId = Guid.NewGuid();

        var added = await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members", new { memberId = memberId.ToString(), positionTitle = "Trustee" });
        added.StatusCode.Should().Be(HttpStatusCode.OK);
        var addedBody = await added.Content.ReadFromJsonAsync<JsonElement>();
        var trustMemberId = addedBody.GetProperty("id").GetGuid();
        addedBody.GetProperty("status").GetString().Should().Be("Active");

        var updated = await Admin().PutAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members/{trustMemberId}/position", new { positionTitle = "Treasurer" });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("positionTitle").GetString()
            .Should().Be("Treasurer");

        var listed = await Member().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/members");
        listed.EnumerateArray().Should().ContainSingle(m => m.GetProperty("id").GetGuid() == trustMemberId);

        var removed = await Admin().DeleteAsync($"/v1/trust/trusts/{trustId}/members/{trustMemberId}");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()
            .Should().Be("Removed");

        // Removing again is success with nothing changed.
        (await Admin().DeleteAsync($"/v1/trust/trusts/{trustId}/members/{trustMemberId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Updating a removed member's position is refused.
        (await Admin().PutAsJsonAsync(
                $"/v1/trust/trusts/{trustId}/members/{trustMemberId}/position", new { positionTitle = "X" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Adding_an_already_active_member_is_a_no_op()
    {
        var trustId = await CreateTrustAsync();
        var memberId = Guid.NewGuid();

        var first = await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members", new { memberId = memberId.ToString(), positionTitle = "Trustee" });
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var second = await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members", new { memberId = memberId.ToString(), positionTitle = "Secretary" });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        secondBody.GetProperty("id").GetGuid().Should().Be(firstId, "the existing active row, unchanged");
        secondBody.GetProperty("positionTitle").GetString().Should().Be("Trustee", "the second add did not overwrite it");

        (await factory.WithDbContextAsync(db =>
            db.TrustMembers.IgnoreQueryFilters().CountAsync(m => m.TrustId == trustId && m.MemberId == memberId)))
            .Should().Be(1);
    }

    [Fact]
    public async Task Re_adding_a_removed_member_reactivates_the_same_row()
    {
        var trustId = await CreateTrustAsync();
        var memberId = Guid.NewGuid();

        var added = await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members", new { memberId = memberId.ToString(), positionTitle = "Trustee" });
        var trustMemberId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await Admin().DeleteAsync($"/v1/trust/trusts/{trustId}/members/{trustMemberId}");

        var readded = await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members", new { memberId = memberId.ToString(), positionTitle = "Treasurer" });
        readded.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await readded.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(trustMemberId, "the same row is reactivated, not a new one");
        body.GetProperty("status").GetString().Should().Be("Active");
        body.GetProperty("positionTitle").GetString().Should().Be("Treasurer");

        (await factory.WithDbContextAsync(db =>
            db.TrustMembers.IgnoreQueryFilters().CountAsync(m => m.TrustId == trustId && m.MemberId == memberId)))
            .Should().Be(1, "at most one row per (Trust, Member) ever");
    }

    [Fact]
    public async Task Another_Samaajs_trust_is_invisible()
    {
        var trustId = await CreateTrustAsync();

        (await Member(OtherTenantId).GetAsync($"/v1/trust/trusts/{trustId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listed = await Member(OtherTenantId).GetFromJsonAsync<JsonElement>("/v1/trust/trusts");
        listed.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Creating_a_trust_announces_it_through_the_outbox()
    {
        var trustId = await CreateTrustAsync();

        var payload = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.Where(m => m.Topic == "trust.trust.created.v1").Select(m => m.Payload).SingleAsync());

        payload.Should().Contain(trustId.ToString());
    }

    [Fact]
    public async Task The_trust_locality_index_is_unique_at_the_database()
    {
        var definition = await IndexDefinitionAsync("trusts", "ix_trusts_tenant_id_locality_id");

        definition.Should().NotBeNull();
        definition.Should().StartWith("CREATE UNIQUE INDEX");
    }

    [Fact]
    public async Task The_trust_member_index_is_unique_at_the_database()
    {
        var definition = await IndexDefinitionAsync("trust_members", "ix_trust_members_trust_id_member_id");

        definition.Should().NotBeNull();
        definition.Should().StartWith("CREATE UNIQUE INDEX");
    }

    private async Task<string?> IndexDefinitionAsync(string table, string index) =>
        await factory.WithDbContextAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            await db.Database.OpenConnectionAsync();
            command.CommandText =
                $"SELECT indexdef FROM pg_indexes WHERE tablename = '{table}' AND indexname = '{index}';";
            return (string?)await command.ExecuteScalarAsync();
        });
}
