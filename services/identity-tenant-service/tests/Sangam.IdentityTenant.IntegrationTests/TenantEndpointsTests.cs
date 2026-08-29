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
        // Naming the grievance contact needs AdminUsers.Manage, because a
        // Samaaj Admin must be able to do it and they do not hold Tenant.Manage.
        factory.CreateClientWith(PermissionKeys.TenantManage, PermissionKeys.AdminUsersManage);

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
        persisted.EnabledModules.Should().ContainSingle().Which.Should().Be("pathshala");

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

    [Fact]
    public async Task The_registration_directory_is_anonymous_and_lists_only_active_Samaaj()
    {
        var admin = SuperAdminClient();

        // One created but never activated, one activated.
        await admin.PostAsJsonAsync(TenantsUrl, NewTenantPayload("dormant-samaj"));

        var live = await admin.PostAsJsonAsync(TenantsUrl, NewTenantPayload("live-samaj"));
        var liveId = (await live.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.PatchAsJsonAsync($"{TenantsUrl}/{liveId}/status", new { status = "Active" });

        var directory = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>($"{TenantsUrl}/directory");

        var slugs = directory.EnumerateArray().Select(t => t.GetProperty("slug").GetString()).ToList();

        slugs.Should().Contain("live-samaj");
        slugs.Should().NotContain("dormant-samaj");
    }

    [Fact]
    public async Task The_registration_directory_does_not_expose_contact_details()
    {
        var admin = SuperAdminClient();
        var created = await admin.PostAsJsonAsync(TenantsUrl, NewTenantPayload("live-samaj"));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.PatchAsJsonAsync($"{TenantsUrl}/{id}/status", new { status = "Active" });

        var directory = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>($"{TenantsUrl}/directory");

        var entry = directory.EnumerateArray().First();

        entry.TryGetProperty("contactEmail", out _).Should().BeFalse();
        entry.TryGetProperty("contactPerson", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_tenant_can_be_resolved_by_id_without_a_token()
    {
        // The gateway calls this on every authenticated request, while deciding
        // whether the request may proceed at all.
        var created = await SuperAdminClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var body = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>($"{TenantsUrl}/by-id/{id}");

        body.GetProperty("slug").GetString().Should().Be("mumbai-samaaj");
        body.GetProperty("status").GetString().Should().Be("Inactive");
    }

    [Fact]
    public async Task Resolving_by_id_reports_the_status_so_the_gateway_can_refuse_an_inactive_Samaaj()
    {
        var admin = SuperAdminClient();
        var created = await admin.PostAsJsonAsync(TenantsUrl, NewTenantPayload());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await admin.PatchAsJsonAsync($"{TenantsUrl}/{id}/status", new { status = "Active" });

        var active = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"{TenantsUrl}/by-id/{id}");
        active.GetProperty("status").GetString().Should().Be("Active");

        // Deactivating re-asks for the password, so this step needs the real
        // Super Admin account. Asserted rather than fired and forgotten: a
        // deactivation that quietly failed used to surface as a baffling
        // "expected Inactive, got Active" two lines below.
        var stepUpAdmin = await factory.CreateSuperAdminClientAsync();

        (await stepUpAdmin.PatchAsJsonAsync(
                $"{TenantsUrl}/{id}/status",
                new { status = "Inactive", password = IdentityTenantApiFactory.BootstrapPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var inactive = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"{TenantsUrl}/by-id/{id}");
        inactive.GetProperty("status").GetString().Should().Be("Inactive");
    }

    [Fact]
    public async Task Resolving_by_id_does_not_expose_contact_details()
    {
        var created = await SuperAdminClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var body = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>($"{TenantsUrl}/by-id/{id}");

        body.TryGetProperty("contactEmail", out _).Should().BeFalse();
        body.TryGetProperty("contactPerson", out _).Should().BeFalse();
    }

    [Fact]
    public async Task An_unknown_tenant_id_is_a_404()
    {
        var response = await factory.CreateClient().GetAsync($"{TenantsUrl}/by-id/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Samaaj_starts_with_no_grievance_contact_and_says_so()
    {
        var created = await SuperAdminClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload());
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();

        // Null rather than an empty object: "nobody has been named" is a real
        // state a Samaaj needs to be able to see about itself (DPDP s.13).
        body.GetProperty("grievanceContact").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_Super_Admin_names_the_grievance_contact_and_it_is_public()
    {
        var admin = SuperAdminClient();
        var created = await admin.PostAsJsonAsync(TenantsUrl, NewTenantPayload());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var set = await admin.PutAsJsonAsync($"{TenantsUrl}/{id}/grievance-contact", new
        {
            name = "Ravi Shah",
            email = "GRIEVANCES@example.com",
            phone = "+919812345678",
        });

        set.StatusCode.Should().Be(HttpStatusCode.OK);

        // Published, per s.13: reachable without a token, like the rest of the
        // public Samaaj summary.
        var summary = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>($"{TenantsUrl}/by-id/{id}");

        var contact = summary.GetProperty("grievanceContact");

        contact.GetProperty("name").GetString().Should().Be("Ravi Shah");
        contact.GetProperty("email").GetString().Should().Be("grievances@example.com");
    }

    [Fact]
    public async Task A_name_with_no_way_to_reach_them_is_refused()
    {
        var admin = SuperAdminClient();
        var created = await admin.PostAsJsonAsync(TenantsUrl, NewTenantPayload());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // A name alone is not a means of redressal.
        var response = await admin.PutAsJsonAsync($"{TenantsUrl}/{id}/grievance-contact", new
        {
            name = "Ravi Shah",
            email = (string?)null,
            phone = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Clearing_the_grievance_contact_is_allowed_because_having_none_is_visible()
    {
        var admin = SuperAdminClient();
        var created = await admin.PostAsJsonAsync(TenantsUrl, NewTenantPayload());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await admin.PutAsJsonAsync($"{TenantsUrl}/{id}/grievance-contact", new
        {
            name = "Ravi Shah",
            email = "grievances@example.com",
            phone = (string?)null,
        });

        var cleared = await admin.PutAsJsonAsync($"{TenantsUrl}/{id}/grievance-contact", new
        {
            name = (string?)null,
            email = (string?)null,
            phone = (string?)null,
        });

        cleared.StatusCode.Should().Be(HttpStatusCode.OK);

        (await cleared.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("grievanceContact").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_member_cannot_name_the_grievance_contact()
    {
        var created = await SuperAdminClient().PostAsJsonAsync(TenantsUrl, NewTenantPayload());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var member = factory.CreateClientAs(Guid.NewGuid(), id, [Roles.Member], []);

        var response = await member.PutAsJsonAsync($"{TenantsUrl}/{id}/grievance-contact", new
        {
            name = "Not Me",
            email = "me@example.com",
            phone = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
