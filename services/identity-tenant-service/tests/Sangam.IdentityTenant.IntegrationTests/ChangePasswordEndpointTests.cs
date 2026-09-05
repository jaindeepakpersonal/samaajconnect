using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Against a real database because the interesting behaviour is a row: does a
/// refresh token issued before the change still work afterwards. That cannot
/// be proven against a substituted <c>ISessionService</c>.
/// </summary>
public sealed class ChangePasswordEndpointTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string Password = "a-long-enough-password";
    private const string Member = "ravi@example.com";
    private const string ChangePasswordUrl = "/v1/identity/me/password";

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

    private sealed record Signed(string AccessToken, string RefreshToken);

    private async Task<Signed> SignInAsync()
    {
        var registered = await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = _slug,
            fullName = "Ravi Shah",
            mobileOrEmail = Member,
            password = Password,
            consentedPurposes = new[] { "Membership" },
            noticeVersion = _noticeVersion,
        });

        registered.StatusCode.Should().Be(HttpStatusCode.Created);

        return await LoginAsync(Password);
    }

    private async Task<Signed> LoginAsync(string password)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = Member,
            password,
        });

        login.EnsureSuccessStatusCode();

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return new Signed(
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!);
    }

    private async Task<HttpResponseMessage> ChangePasswordAsync(
        string accessToken, string currentPassword, string newPassword)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);

        return await client.PostAsJsonAsync(
            ChangePasswordUrl, new { currentPassword, newPassword });
    }

    [Fact]
    public async Task Changes_the_password_and_the_new_one_signs_in()
    {
        var signed = await SignInAsync();

        (await ChangePasswordAsync(signed.AccessToken, Password, "a-different-long-password"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
                new { mobileOrEmail = Member, password = "a-different-long-password" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_old_password_no_longer_signs_in()
    {
        var signed = await SignInAsync();

        await ChangePasswordAsync(signed.AccessToken, Password, "a-different-long-password");

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
                new { mobileOrEmail = Member, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_current_password_is_refused_and_changes_nothing()
    {
        var signed = await SignInAsync();

        var response = await ChangePasswordAsync(signed.AccessToken, "not-the-password", "a-different-long-password");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
                new { mobileOrEmail = Member, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_weak_new_password_is_rejected_with_a_field_level_message()
    {
        var signed = await SignInAsync();

        var response = await ChangePasswordAsync(signed.AccessToken, Password, "short");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors").TryGetProperty("NewPassword", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Requires_a_token()
    {
        (await factory.CreateClient().PostAsJsonAsync(
                ChangePasswordUrl, new { currentPassword = Password, newPassword = "a-different-long-password" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_refresh_token_issued_before_the_change_no_longer_works()
    {
        // This is the whole reason ISessionService.EndAllForUserAsync is called
        // here: a stolen but still-valid refresh token is worth nothing once
        // its owner changes their password, and the only way to prove that is
        // to show the row this test reads back has actually been revoked.
        var signed = await SignInAsync();

        await ChangePasswordAsync(signed.AccessToken, Password, "a-different-long-password");

        (await factory.CreateClient().PostAsJsonAsync(
                "/v1/identity/token/refresh", new { refreshToken = signed.RefreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var revoked = await factory.WithDbContextAsync(db =>
            db.RefreshTokens.AsNoTracking().Select(t => t.RevokedAt).SingleAsync());

        revoked.Should().NotBeNull();
    }

    [Fact]
    public async Task Writes_a_PasswordChanged_event_to_the_outbox()
    {
        var signed = await SignInAsync();

        await ChangePasswordAsync(signed.AccessToken, Password, "a-different-long-password");

        var topics = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().Select(m => m.Topic).ToListAsync());

        topics.Should().Contain("identity.user.password-changed.v1");
    }
}
