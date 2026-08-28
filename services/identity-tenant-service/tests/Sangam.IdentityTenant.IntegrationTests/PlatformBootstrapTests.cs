using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Walks the whole Stage 0 path with no hand-minted tokens: bootstrap a Super
/// Admin, sign in as them, create and activate a Samaaj, then register and sign
/// in as an ordinary member of it.
/// </summary>
public sealed class PlatformBootstrapTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        await factory.BootstrapSuperAdminAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<JsonElement> LoginAsync(string identifier, string password)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/v1/identity/login", new { mobileOrEmail = identifier, password });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static HttpClient Authorized(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    [Fact]
    public async Task The_bootstrapped_Super_Admin_can_sign_in_without_belonging_to_a_Samaaj()
    {
        var body = await LoginAsync(
            IdentityTenantApiFactory.BootstrapIdentifier, IdentityTenantApiFactory.BootstrapPassword);

        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString())
            .Should().Contain("SuperAdmin");

        // A platform account has no Samaaj subdomain to be redirected to.
        body.GetProperty("tenantSlug").GetString().Should().BeEmpty();
        body.GetProperty("tenantId").GetGuid().Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task Bootstrapping_twice_does_not_create_a_second_Super_Admin()
    {
        await factory.BootstrapSuperAdminAsync();

        var admins = await factory.WithDbContextAsync(db => db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => u.MobileOrEmail == IdentityTenantApiFactory.BootstrapIdentifier));

        admins.Should().Be(1);
    }

    [Fact]
    public async Task A_Super_Admin_token_carries_every_seeded_permission()
    {
        var login = await LoginAsync(
            IdentityTenantApiFactory.BootstrapIdentifier, IdentityTenantApiFactory.BootstrapPassword);

        var client = Authorized(factory.CreateClient(), login.GetProperty("accessToken").GetString()!);

        var me = await client.GetFromJsonAsync<JsonElement>("/v1/identity/me");

        me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Contain("Tenant.Manage").And.Contain("Audit.Read");
    }

    [Fact]
    public async Task A_Super_Admin_can_stand_up_a_Samaaj_that_a_member_then_joins_and_signs_into()
    {
        var adminLogin = await LoginAsync(
            IdentityTenantApiFactory.BootstrapIdentifier, IdentityTenantApiFactory.BootstrapPassword);

        var admin = Authorized(factory.CreateClient(), adminLogin.GetProperty("accessToken").GetString()!);

        var created = await admin.PostAsJsonAsync("/v1/identity/tenants", new
        {
            name = "Mahavir Samaaj",
            slug = "mahavir-samaj",
            enabledModules = new[] { "Pathshala" },
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var tenantId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var activated = await admin.PatchAsJsonAsync(
            $"/v1/identity/tenants/{tenantId}/status", new { status = "Active" });

        activated.StatusCode.Should().Be(HttpStatusCode.OK);

        var registered = await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = "mahavir-samaj",
            fullName = "Ravi Shah",
            mobileOrEmail = "ravi@example.com",
            password = "a-long-enough-password",
        });

        registered.StatusCode.Should().Be(HttpStatusCode.Created);

        var memberLogin = await LoginAsync("ravi@example.com", "a-long-enough-password");

        // The portal uses this slug to send the member to their own subdomain.
        memberLogin.GetProperty("tenantSlug").GetString().Should().Be("mahavir-samaj");
        memberLogin.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
    }
}
