using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Against a real database for the same reason <see cref="LoginOtpEndpointTests"/>
/// is: the plaintext code exists nowhere else to read back.
/// </summary>
public sealed class PasswordResetEndpointTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string Password = "a-long-enough-password";
    private const string NewPassword = "a-different-long-password";
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

    private async Task<string> SignInAsync(string password = Password)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
            new { mobileOrEmail = Member, password });

        login.EnsureSuccessStatusCode();

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("refreshToken").GetString()!;
    }

    private Task<HttpResponseMessage> RequestResetAsync(string identifier = Member) =>
        factory.CreateClient().PostAsJsonAsync(
            "/v1/identity/password-reset/request", new { mobileOrEmail = identifier });

    private Task<HttpResponseMessage> RedeemResetAsync(
        string code, string newPassword = NewPassword, string identifier = Member) =>
        factory.CreateClient().PostAsJsonAsync(
            "/v1/identity/password-reset/redeem",
            new { mobileOrEmail = identifier, code, newPassword });

    private async Task<string> LatestResetCodeAsync()
    {
        var payload = await factory.WithDbContextAsync(db => db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Topic == "identity.password-reset.requested.v1")
            .OrderByDescending(m => m.OccurredAt)
            .Select(m => m.Payload)
            .FirstAsync());

        return JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Requesting_a_reset_for_a_real_account_writes_the_code_to_the_outbox()
    {
        await RegisterAsync();

        (await RequestResetAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        (await LatestResetCodeAsync()).Should().MatchRegex("^[0-9]{6}$");
    }

    [Fact]
    public async Task An_unknown_identifier_gets_the_same_200_and_writes_nothing()
    {
        (await RequestResetAsync("ghost@example.com")).StatusCode.Should().Be(HttpStatusCode.OK);

        var wrote = await factory.WithDbContextAsync(db => db.OutboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.Topic == "identity.password-reset.requested.v1"));

        wrote.Should().BeFalse();
    }

    [Fact]
    public async Task The_code_sets_a_new_password_with_no_token_in_the_response()
    {
        await RegisterAsync();
        await RequestResetAsync();

        var code = await LatestResetCodeAsync();
        var response = await RedeemResetAsync(code);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.TryGetProperty("accessToken", out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_old_password_no_longer_works_and_the_new_one_does()
    {
        await RegisterAsync();
        await RequestResetAsync();

        var code = await LatestResetCodeAsync();
        await RedeemResetAsync(code);

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
                new { mobileOrEmail = Member, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
                new { mobileOrEmail = Member, password = NewPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_refresh_token_issued_before_the_reset_no_longer_works()
    {
        await RegisterAsync();
        var refreshToken = await SignInAsync();

        await RequestResetAsync();
        var code = await LatestResetCodeAsync();
        await RedeemResetAsync(code);

        (await factory.CreateClient().PostAsJsonAsync(
                "/v1/identity/token/refresh", new { refreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_code_cannot_be_redeemed_twice()
    {
        await RegisterAsync();
        await RequestResetAsync();

        var code = await LatestResetCodeAsync();
        await RedeemResetAsync(code);

        (await RedeemResetAsync(code, newPassword: "yet-another-long-password"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_weak_new_password_is_rejected_with_a_field_level_message()
    {
        await RegisterAsync();
        await RequestResetAsync();

        var code = await LatestResetCodeAsync();
        var response = await RedeemResetAsync(code, newPassword: "short");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors").TryGetProperty("NewPassword", out _).Should().BeTrue();
    }
}
