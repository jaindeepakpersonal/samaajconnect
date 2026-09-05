using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Against a real database because the plaintext code exists nowhere else to
/// read back - it is never returned in the HTTP response, only carried on the
/// outbox row this test reads directly, the same way the notification
/// pipeline itself would.
/// </summary>
public sealed class LoginOtpEndpointTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string Password = "a-long-enough-password";
    private const string Member = "ravi@example.com";

    private string _slug = null!;
    private string _noticeVersion = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();

        _slug = (await factory.SeedActiveTenantAsync()).Slug;

        var notice = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/v1/identity/consent-notice");

        _noticeVersion = notice.GetProperty("version").GetString()!;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task RegisterAsync()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = _slug,
            fullName = "Ravi Shah",
            mobileOrEmail = Member,
            password = Password,
            consentedPurposes = new[] { "Membership" },
            noticeVersion = _noticeVersion,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private Task<HttpResponseMessage> RequestOtpAsync(string identifier = Member) =>
        factory.CreateClient().PostAsJsonAsync("/v1/identity/otp/request", new { mobileOrEmail = identifier });

    private Task<HttpResponseMessage> LoginWithOtpAsync(string code, string identifier = Member) =>
        factory.CreateClient().PostAsJsonAsync("/v1/identity/otp/login", new { mobileOrEmail = identifier, code });

    /// <summary>
    /// The only way to get the plaintext: read it off the outbox row this
    /// service itself would hand to the notification pipeline.
    /// </summary>
    private async Task<string> LatestOtpCodeAsync()
    {
        var payload = await factory.WithDbContextAsync(db => db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Topic == "identity.login-otp.requested.v1")
            .OrderByDescending(m => m.OccurredAt)
            .Select(m => m.Payload)
            .FirstAsync());

        return JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Requesting_a_code_for_a_real_account_writes_it_to_the_outbox()
    {
        await RegisterAsync();

        (await RequestOtpAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        var code = await LatestOtpCodeAsync();

        code.Should().MatchRegex("^[0-9]{6}$");
    }

    [Fact]
    public async Task An_unknown_identifier_gets_the_same_200_and_writes_nothing()
    {
        (await RequestOtpAsync("ghost@example.com")).StatusCode.Should().Be(HttpStatusCode.OK);

        var wrote = await factory.WithDbContextAsync(db => db.OutboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.Topic == "identity.login-otp.requested.v1"));

        wrote.Should().BeFalse();
    }

    [Fact]
    public async Task The_code_signs_a_member_in()
    {
        await RegisterAsync();
        await RequestOtpAsync();

        var code = await LatestOtpCodeAsync();
        var response = await LoginWithOtpAsync(code);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("tenantSlug").GetString().Should().Be(_slug);
    }

    [Fact]
    public async Task Signing_in_by_code_verifies_the_contact_address()
    {
        await RegisterAsync();
        await RequestOtpAsync();

        var code = await LatestOtpCodeAsync();
        await LoginWithOtpAsync(code);

        var verified = await factory.WithDbContextAsync(db => db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.MobileOrEmail == Member)
            .Select(u => u.IsContactVerified)
            .SingleAsync());

        verified.Should().BeTrue();
    }

    [Fact]
    public async Task The_code_cannot_be_used_twice()
    {
        await RegisterAsync();
        await RequestOtpAsync();

        var code = await LatestOtpCodeAsync();
        await LoginWithOtpAsync(code);

        (await LoginWithOtpAsync(code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_code_and_an_unknown_account_are_both_401()
    {
        await RegisterAsync();
        await RequestOtpAsync();

        (await LoginWithOtpAsync("000000")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginWithOtpAsync("000000", "ghost@example.com")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Repeated_wrong_codes_lock_the_account_out_of_password_login_too()
    {
        await RegisterAsync();
        await RequestOtpAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LoginWithOtpAsync("000000");
        }

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
                new { mobileOrEmail = Member, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
