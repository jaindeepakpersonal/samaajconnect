using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Security;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

public sealed class AuthEndpointsTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string RegisterUrl = "/v1/identity/register";
    private const string LoginUrl = "/v1/identity/login";
    private const string MeUrl = "/v1/identity/me";
    private const string Password = "a-long-enough-password";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object RegisterPayload(string slug, string identifier) => new
    {
        tenantSlug = slug,
        fullName = "Ravi Shah",
        mobileOrEmail = identifier,
        password = Password,
    };

    private Task<HttpResponseMessage> RegisterAsync(string slug, string identifier = "ravi@example.com") =>
        factory.CreateClient().PostAsJsonAsync(RegisterUrl, RegisterPayload(slug, identifier));

    private Task<HttpResponseMessage> LoginAsync(string identifier, string password) =>
        factory.CreateClient().PostAsJsonAsync(LoginUrl, new { mobileOrEmail = identifier, password });

    private async Task<string> LoginForTokenAsync()
    {
        var response = await LoginAsync("ravi@example.com", Password);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task A_member_can_register_into_an_active_Samaaj()
    {
        var tenant = await factory.SeedActiveTenantAsync();

        var response = await RegisterAsync(tenant.Slug);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("tenantId").GetGuid().Should().Be(tenant.Id);
        body.GetProperty("mobileOrEmail").GetString().Should().Be("ravi@example.com");
        body.GetProperty("isContactVerified").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Registration_writes_a_UserRegistered_event_to_the_outbox()
    {
        var tenant = await factory.SeedActiveTenantAsync();

        await RegisterAsync(tenant.Slug);

        var topics = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().Select(m => m.Topic).ToListAsync());

        topics.Should().Contain("identity.user.registered.v1");
    }

    [Fact]
    public async Task Registration_into_a_Samaaj_that_is_not_active_yet_is_refused()
    {
        var client = factory.CreateClientWith(PermissionKeys.TenantManage);
        await client.PostAsJsonAsync("/v1/identity/tenants", new { name = "Pune Samaaj", slug = "pune-samaaj" });

        var response = await RegisterAsync("pune-samaaj");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Registration_into_an_unknown_Samaaj_is_a_404()
    {
        (await RegisterAsync("no-such-samaaj")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task One_identifier_cannot_be_registered_in_two_different_Samaaj()
    {
        var first = await factory.SeedActiveTenantAsync("mumbai-samaaj");
        var second = await factory.SeedActiveTenantAsync("pune-samaaj");

        (await RegisterAsync(first.Slug)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await RegisterAsync(second.Slug)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_short_password_is_rejected_with_a_field_level_message()
    {
        var tenant = await factory.SeedActiveTenantAsync();

        var response = await factory.CreateClient().PostAsJsonAsync(RegisterUrl, new
        {
            tenantSlug = tenant.Slug,
            fullName = "Ravi Shah",
            mobileOrEmail = "ravi@example.com",
            password = "short",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors").TryGetProperty("Password", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_registered_member_can_log_in_and_is_told_which_Samaaj_to_go_to()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        await RegisterAsync(tenant.Slug);

        var response = await LoginAsync("RAVI@example.com", Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("tenantSlug").GetString().Should().Be(tenant.Slug);
        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("Member");
    }

    [Fact]
    public async Task The_issued_token_works_against_me_and_carries_the_seeded_permissions()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        await RegisterAsync(tenant.Slug);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await LoginForTokenAsync());

        var me = await client.GetFromJsonAsync<JsonElement>(MeUrl);

        me.GetProperty("mobileOrEmail").GetString().Should().Be("ravi@example.com");
        me.GetProperty("tenantSlug").GetString().Should().Be(tenant.Slug);
        me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Contain("Member");

        // These come from the migration-seeded role_permissions rows.
        me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().Contain("Members.Read").And.Contain("Timeline.Post");
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        (await factory.CreateClient().GetAsync(MeUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_account_and_a_wrong_password_produce_the_same_response()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        await RegisterAsync(tenant.Slug);

        var wrongPassword = await LoginAsync("ravi@example.com", "not-the-password");
        var unknownAccount = await LoginAsync("ghost@example.com", "not-the-password");

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknownAccount.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Compared field by field rather than as raw strings: the problem
        // document carries a per-request traceId that is legitimately different.
        var wrongBody = await wrongPassword.Content.ReadFromJsonAsync<JsonElement>();
        var unknownBody = await unknownAccount.Content.ReadFromJsonAsync<JsonElement>();

        unknownBody.GetProperty("title").GetString()
            .Should().Be(wrongBody.GetProperty("title").GetString());
        unknownBody.GetProperty("detail").GetString()
            .Should().Be(wrongBody.GetProperty("detail").GetString());
        unknownBody.GetProperty("status").GetInt32()
            .Should().Be(wrongBody.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task A_failed_attempt_is_persisted_even_though_the_command_rolls_back()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        await RegisterAsync(tenant.Slug);

        await LoginAsync("ravi@example.com", "not-the-password");

        // This is the whole reason IFailedLoginRecorder exists: the login
        // command returned a failure, so its transaction rolled back, and a
        // counter written on the tracked aggregate would have vanished with it.
        var attempts = await factory.WithDbContextAsync(db => db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(u => u.FailedLoginAttempts)
            .SingleAsync());

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_lock_the_account()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        await RegisterAsync(tenant.Slug);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LoginAsync("ravi@example.com", "not-the-password");
        }

        // Even the correct password is refused while the lockout holds.
        (await LoginAsync("ravi@example.com", Password))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_of_a_deactivated_Samaaj_cannot_log_in()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        await RegisterAsync(tenant.Slug);

        var admin = factory.CreateClientWith(PermissionKeys.TenantManage);
        await admin.PatchAsJsonAsync($"/v1/identity/tenants/{tenant.Id}/status", new { status = "Inactive" });

        (await LoginAsync("ravi@example.com", Password))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_for_one_Samaaj_cannot_be_pointed_at_another_with_a_header()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        await RegisterAsync(tenant.Slug);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await LoginForTokenAsync());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        // The signed claim outranks the header, and disagreement is refused
        // rather than quietly resolved in either direction.
        (await client.GetAsync(MeUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_matching_tenant_header_alongside_the_token_is_accepted()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        await RegisterAsync(tenant.Slug);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", await LoginForTokenAsync());
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant.Id.ToString());

        (await client.GetAsync(MeUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
