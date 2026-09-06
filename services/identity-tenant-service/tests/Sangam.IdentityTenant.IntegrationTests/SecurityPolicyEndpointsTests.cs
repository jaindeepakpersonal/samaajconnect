using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.IdentityTenant.Application.Security;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// The admin Settings screen's Security card: a Samaaj's own overrides to the
/// platform's password/OTP/lockout/session constants.
/// </summary>
public sealed class SecurityPolicyEndpointsTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string Password = "a-long-enough-password";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient SamaajAdminClientFor(Guid tenantId) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId, ["SamaajAdmin"], [PermissionKeys.TenantManageSecurityPolicy]);

    private static string Url(Guid tenantId) => $"/v1/identity/tenants/{tenantId}/security-policy";

    [Fact]
    public async Task A_Samaaj_starts_with_no_overrides_at_all()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        var admin = SamaajAdminClientFor(tenant.Id);

        var body = await admin.GetFromJsonAsync<JsonElement>(Url(tenant.Id));

        body.GetProperty("minPasswordLength").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("maxFailedLoginAttempts").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_Samaaj_Admin_sets_their_own_Samaajs_policy_and_reads_it_back()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        var admin = SamaajAdminClientFor(tenant.Id);

        var set = await admin.PutAsJsonAsync(Url(tenant.Id), new
        {
            minPasswordLength = 14,
            otpLifetimeMinutes = 5,
            maxFailedLoginAttempts = 3,
            lockoutDurationMinutes = 30,
            refreshTokenLifetimeDays = 7,
        });

        set.StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await admin.GetFromJsonAsync<JsonElement>(Url(tenant.Id));

        read.GetProperty("minPasswordLength").GetInt32().Should().Be(14);
        read.GetProperty("otpLifetimeMinutes").GetInt32().Should().Be(5);
        read.GetProperty("maxFailedLoginAttempts").GetInt32().Should().Be(3);
        read.GetProperty("lockoutDurationMinutes").GetInt32().Should().Be(30);
        read.GetProperty("refreshTokenLifetimeDays").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task Clearing_a_field_sends_null_and_it_reads_back_null()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        var admin = SamaajAdminClientFor(tenant.Id);

        await admin.PutAsJsonAsync(Url(tenant.Id), new
        {
            minPasswordLength = 14,
            otpLifetimeMinutes = (int?)null,
            maxFailedLoginAttempts = (int?)null,
            lockoutDurationMinutes = (int?)null,
            refreshTokenLifetimeDays = (int?)null,
        });

        var cleared = await admin.PutAsJsonAsync(Url(tenant.Id), new
        {
            minPasswordLength = (int?)null,
            otpLifetimeMinutes = (int?)null,
            maxFailedLoginAttempts = (int?)null,
            lockoutDurationMinutes = (int?)null,
            refreshTokenLifetimeDays = (int?)null,
        });

        cleared.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("minPasswordLength").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_password_length_below_the_platform_floor_is_refused()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        var admin = SamaajAdminClientFor(tenant.Id);

        var response = await admin.PutAsJsonAsync(Url(tenant.Id), new
        {
            minPasswordLength = 4,
            otpLifetimeMinutes = (int?)null,
            maxFailedLoginAttempts = (int?)null,
            lockoutDurationMinutes = (int?)null,
            refreshTokenLifetimeDays = (int?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_plain_member_cannot_set_the_policy()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        var member = factory.CreateClientAs(Guid.NewGuid(), tenant.Id, ["Member"], []);

        var response = await member.PutAsJsonAsync(Url(tenant.Id), new
        {
            minPasswordLength = 14,
            otpLifetimeMinutes = (int?)null,
            maxFailedLoginAttempts = (int?)null,
            lockoutDurationMinutes = (int?)null,
            refreshTokenLifetimeDays = (int?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Samaaj_Admin_cannot_set_another_Samaajs_policy()
    {
        var ownTenant = await factory.SeedActiveTenantAsync("own-samaaj");
        var otherTenant = await factory.SeedActiveTenantAsync("other-samaaj");
        var admin = SamaajAdminClientFor(ownTenant.Id);

        var response = await admin.PutAsJsonAsync(Url(otherTenant.Id), new
        {
            minPasswordLength = 14,
            otpLifetimeMinutes = (int?)null,
            maxFailedLoginAttempts = (int?)null,
            lockoutDurationMinutes = (int?)null,
            refreshTokenLifetimeDays = (int?)null,
        });

        // Not found, not forbidden - the same as every other self-tenant-only
        // write (the grievance contact, the logo): confirming a Samaaj exists
        // to someone who may not act on it is its own small leak.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_Super_Admin_can_set_any_Samaajs_policy()
    {
        var tenant = await factory.SeedActiveTenantAsync();
        var superAdmin = factory.CreateClientAs(
            Guid.NewGuid(), ["SuperAdmin"], [PermissionKeys.TenantManageSecurityPolicy]);

        var response = await superAdmin.PutAsJsonAsync(Url(tenant.Id), new
        {
            minPasswordLength = 14,
            otpLifetimeMinutes = (int?)null,
            maxFailedLoginAttempts = (int?)null,
            lockoutDurationMinutes = (int?)null,
            refreshTokenLifetimeDays = (int?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- The policy actually changes behaviour --------------------------------

    private static object RegisterPayload(string slug, string identifier) => new
    {
        tenantSlug = slug,
        fullName = "Ravi Shah",
        mobileOrEmail = identifier,
        password = Password,
        consentedPurposes = new[] { "Membership" },
        noticeVersion = Domain.Consents.ConsentNotice.CurrentVersion,
    };

    private Task<HttpResponseMessage> LoginAsync(string identifier, string password) =>
        factory.CreateClient().PostAsJsonAsync(
            "/v1/identity/login", new { mobileOrEmail = identifier, password });

    [Fact]
    public async Task A_tighter_lockout_threshold_locks_the_account_sooner_than_the_platform_default()
    {
        var tenant = await factory.SeedActiveTenantAsync();

        var setResponse = await SamaajAdminClientFor(tenant.Id).PutAsJsonAsync(Url(tenant.Id), new
        {
            minPasswordLength = (int?)null,
            otpLifetimeMinutes = (int?)null,
            maxFailedLoginAttempts = 3,
            lockoutDurationMinutes = (int?)null,
            refreshTokenLifetimeDays = (int?)null,
        });
        setResponse.StatusCode.Should().Be(HttpStatusCode.OK, await setResponse.Content.ReadAsStringAsync());

        var registerResponse = await factory.CreateClient().PostAsJsonAsync(
            "/v1/identity/register", RegisterPayload(tenant.Slug, "ravi@example.com"));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created, await registerResponse.Content.ReadAsStringAsync());

        // The platform default is five; this Samaaj asked for three.
        await LoginAsync("ravi@example.com", "wrong-password-1");
        await LoginAsync("ravi@example.com", "wrong-password-2");
        await LoginAsync("ravi@example.com", "wrong-password-3");

        // Even the correct password is refused while the lockout holds.
        (await LoginAsync("ravi@example.com", Password))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
