using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Security;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

public sealed class TenantEndpointsTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string TenantsUrl = "/v1/identity/tenants";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object NewTenantPayload(string slug = "mumbai-samaaj") => new
    {
        name = "Mumbai Samaaj",
        slug,
        domain = (string?)null,
        contactPerson = "Ravi Shah",
        contactEmail = "ravi@example.com",
        enabledModules = new[] { "Pathshala" },
    };

    private HttpClient SuperAdminClient() =>
        factory.CreateClientWith(PermissionKeys.TenantManage);

    [Fact]
    public async Task Creating_a_tenant_without_a_token_is_rejected()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Creating_a_tenant_as_a_plain_member_is_forbidden()
    {
        var client = factory.CreateClientAs(Guid.NewGuid(), [Roles.Member], []);

        var response = await client.PostAsJsonAsync(TenantsUrl, NewTenantPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Creating_a_tenant_as_super_admin_without_the_permission_is_forbidden()
    {
        var client = factory.CreateClientAs(Guid.NewGuid(), [Roles.SuperAdmin], []);

        var response = await client.PostAsJsonAsync(TenantsUrl, NewTenantPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Creating_a_tenant_persists_it_and_writes_exactly_one_outbox_row_in_the_same_transaction()
    {
        var response = await SuperAdminClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith("/v1/identity/tenants/mumbai-samaaj");

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var tenantId = created.GetProperty("id").GetGuid();

        var persisted = await factory.WithDbContextAsync(db =>
            db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId));

        persisted.Slug.Should().Be("mumbai-samaaj");
        persisted.EnabledModules.Should().ContainSingle().Which.Should().Be("Pathshala");

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().ToListAsync());

        outbox.Should().ContainSingle();
        outbox[0].Topic.Should().Be("identity.tenant.created.v1");
        outbox[0].TenantId.Should().Be(tenantId);
        outbox[0].ProcessedAt.Should().BeNull();
        outbox[0].Payload.Should().Contain("mumbai-samaaj");
    }

    [Fact]
    public async Task A_rejected_duplicate_slug_leaves_the_first_tenant_and_its_single_outbox_row_untouched()
    {
        var client = SuperAdminClient();

        (await client.PostAsJsonAsync(TenantsUrl, NewTenantPayload()))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync(TenantsUrl, NewTenantPayload());

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var tenants = await factory.WithDbContextAsync(db => db.Tenants.AsNoTracking().CountAsync());
        var outbox = await factory.WithDbContextAsync(db => db.OutboxMessages.AsNoTracking().CountAsync());

        tenants.Should().Be(1);
        outbox.Should().Be(1);
    }

    [Fact]
    public async Task A_slug_differing_only_in_case_collides_with_an_existing_tenant()
    {
        var client = SuperAdminClient();

        await client.PostAsJsonAsync(TenantsUrl, NewTenantPayload("mumbai-samaaj"));

        var response = await client.PostAsJsonAsync(TenantsUrl, NewTenantPayload("Mumbai-Samaaj"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_invalid_slug_returns_a_validation_problem_naming_the_field()
    {
        var response = await SuperAdminClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload("not a slug"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors").TryGetProperty("Slug", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Resolving_a_slug_needs_no_token_because_the_gateway_calls_it_before_auth_exists()
    {
        await SuperAdminClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload());

        var response = await factory.CreateClient().GetAsync($"{TenantsUrl}/mumbai-samaaj");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("slug").GetString().Should().Be("mumbai-samaaj");
        body.GetProperty("name").GetString().Should().Be("Mumbai Samaaj");
    }

    [Fact]
    public async Task Slug_resolution_does_not_leak_the_Samaaj_contact_details_to_anonymous_callers()
    {
        await SuperAdminClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload());

        var body = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>($"{TenantsUrl}/mumbai-samaaj");

        body.TryGetProperty("contactEmail", out _).Should().BeFalse();
        body.TryGetProperty("contactPerson", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_tenant_override_header_from_a_non_Super_Admin_is_refused()
    {
        var client = factory.CreateClientAs(Guid.NewGuid(), [Roles.Member], [PermissionKeys.TenantManage]);
        client.DefaultRequestHeaders.Add("X-Tenant-Override-Id", Guid.NewGuid().ToString());

        var response = await client.PostAsJsonAsync(TenantsUrl, NewTenantPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_unknown_slug_returns_404()
    {
        var response = await factory.CreateClient().GetAsync($"{TenantsUrl}/no-such-samaaj");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
