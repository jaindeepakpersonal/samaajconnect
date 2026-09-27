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
    public async Task A_trust_is_renamed_by_an_administrator_and_not_by_a_member()
    {
        var trustId = await CreateTrustAsync(name: "Hiran Magri Trst");

        var renamed = await Admin().PatchAsJsonAsync($"/v1/trust/trusts/{trustId}", new { name = "Hiran Magri Trust" });

        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await renamed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString()
            .Should().Be("Hiran Magri Trust");

        var events = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.Where(m => m.Topic == "trust.trust.renamed.v1").CountAsync());
        events.Should().Be(1);

        (await Member().PatchAsJsonAsync($"/v1/trust/trusts/{trustId}", new { name = "Mine" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Admin(OtherTenantId).PatchAsJsonAsync($"/v1/trust/trusts/{trustId}", new { name = "Theirs" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
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

    private async Task<Guid> DefineRoleAsync(Guid trustId, string title, int seats)
    {
        var response = await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/roles", new { title, seats });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> AddMemberAsync(Guid trustId, Guid memberId, Guid? roleId = null) =>
        await Admin().PostAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members", new { memberId = memberId.ToString(), roleId = roleId?.ToString() });

    [Fact]
    public async Task Adding_updating_and_removing_a_member_walks_the_roster()
    {
        var trustId = await CreateTrustAsync();
        var treasurer = await DefineRoleAsync(trustId, "Treasurer", seats: 1);
        var memberId = Guid.NewGuid();

        var added = await AddMemberAsync(trustId, memberId);
        added.StatusCode.Should().Be(HttpStatusCode.OK);
        var addedBody = await added.Content.ReadFromJsonAsync<JsonElement>();
        var trustMemberId = addedBody.GetProperty("id").GetGuid();
        addedBody.GetProperty("status").GetString().Should().Be("Active");
        addedBody.GetProperty("positionTitle").GetString().Should().Be("Trustee", "Trustee unless given another role");

        var updated = await Admin().PutAsJsonAsync(
            $"/v1/trust/trusts/{trustId}/members/{trustMemberId}/role", new { roleId = treasurer });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBody = await updated.Content.ReadFromJsonAsync<JsonElement>();
        updatedBody.GetProperty("positionTitle").GetString().Should().Be("Treasurer");
        updatedBody.GetProperty("roleId").GetGuid().Should().Be(treasurer);

        var listed = await Member().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/members");
        listed.EnumerateArray().Should().ContainSingle(m => m.GetProperty("id").GetGuid() == trustMemberId);

        var removed = await Admin().DeleteAsync($"/v1/trust/trusts/{trustId}/members/{trustMemberId}");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString()
            .Should().Be("Removed");

        // Removing again is success with nothing changed.
        (await Admin().DeleteAsync($"/v1/trust/trusts/{trustId}/members/{trustMemberId}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Moving a removed member into a role is refused.
        (await Admin().PutAsJsonAsync(
                $"/v1/trust/trusts/{trustId}/members/{trustMemberId}/role", new { roleId = treasurer }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_role_takes_no_more_holders_than_it_has_seats()
    {
        var trustId = await CreateTrustAsync();
        var treasurer = await DefineRoleAsync(trustId, "Treasurer", seats: 1);

        (await AddMemberAsync(trustId, Guid.NewGuid(), treasurer)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await AddMemberAsync(trustId, Guid.NewGuid(), treasurer))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "the one seat is taken");

        var second = await (await AddMemberAsync(trustId, Guid.NewGuid()))
            .Content.ReadFromJsonAsync<JsonElement>();

        (await Admin().PutAsJsonAsync(
                $"/v1/trust/trusts/{trustId}/members/{second.GetProperty("id").GetGuid()}/role",
                new { roleId = treasurer }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Resizing below the holders is refused; above is fine.
        (await Admin().PutAsJsonAsync($"/v1/trust/trusts/{trustId}/roles/{treasurer}", new { title = "Treasurer", seats = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var roles = await Member().GetFromJsonAsync<JsonElement>($"/v1/trust/trusts/{trustId}/roles");
        var listed = roles.EnumerateArray().ToList();
        listed[0].GetProperty("title").GetString().Should().Be("Trustee", "Trustee is listed first");
        listed[0].GetProperty("holders").GetInt32().Should().Be(1);
        listed[1].GetProperty("seats").GetInt32().Should().Be(2);
        listed[1].GetProperty("holders").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Roles_are_managed_by_an_administrator_only()
    {
        var trustId = await CreateTrustAsync();

        (await Member().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/roles", new { title = "President", seats = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var president = await DefineRoleAsync(trustId, "President", seats: 1);

        (await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/roles", new { title = "president", seats = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "role titles are unique on a Trust");

        (await Admin().PostAsJsonAsync($"/v1/trust/trusts/{trustId}/roles", new { title = "", seats = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await Admin(OtherTenantId).DeleteAsync($"/v1/trust/trusts/{trustId}/roles/{president}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Admin().DeleteAsync($"/v1/trust/trusts/{trustId}/roles/{president}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.Where(m => m.Topic.StartsWith("trust.role.")).Select(m => m.Topic).ToListAsync());
        events.Should().BeEquivalentTo(["trust.role.defined.v1", "trust.role.removed.v1"]);
    }

    [Fact]
    public async Task Adding_an_already_active_member_is_a_no_op()
    {
        var trustId = await CreateTrustAsync();
        var memberId = Guid.NewGuid();

        var secretary = await DefineRoleAsync(trustId, "Secretary", seats: 1);

        var first = await AddMemberAsync(trustId, memberId);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var second = await AddMemberAsync(trustId, memberId, secretary);
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

        var treasurer = await DefineRoleAsync(trustId, "Treasurer", seats: 1);

        var added = await AddMemberAsync(trustId, memberId);
        var trustMemberId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await Admin().DeleteAsync($"/v1/trust/trusts/{trustId}/members/{trustMemberId}");

        var readded = await AddMemberAsync(trustId, memberId, treasurer);
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
