using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Sessions: rotation, reuse detection, and the revocation that
/// SECURITY-CHECKLIST.md asked for.
/// </summary>
/// <remarks>
/// Against a real database because the interesting behaviour is all in rows.
/// A refresh token is single-use, and what happens when a used one comes back
/// is the whole point of the design - it cannot be tested against a substituted
/// repository, because the substitute is the thing that would have to remember.
/// </remarks>
public sealed class SessionEndpointsTests(IdentityTenantApiFactory factory)
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

        return await LoginAsync();
    }

    private async Task<Signed> LoginAsync()
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = Member,
            password = Password,
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();

        return new Signed(
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!);
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        factory.CreateClient().PostAsJsonAsync("/v1/identity/token/refresh", new { refreshToken });

    private Task<HttpResponseMessage> SignOutAsync(string refreshToken, bool everywhere = false) =>
        factory.CreateClient().PostAsJsonAsync("/v1/identity/logout", new { refreshToken, everywhere });

    // ---- Issuing ----------------------------------------------------------

    [Fact]
    public async Task Signing_in_returns_a_refresh_token_and_stores_only_its_hash()
    {
        var signed = await SignInAsync();

        signed.RefreshToken.Should().NotBeNullOrWhiteSpace();

        var stored = await factory.WithDbContextAsync(db =>
            db.RefreshTokens.AsNoTracking().SingleAsync());

        // A copy of this table must not be a set of working sessions.
        stored.TokenHash.Should().NotBe(signed.RefreshToken);
        stored.RevokedAt.Should().BeNull();
        stored.UsedAt.Should().BeNull();
    }

    // ---- Rotation ---------------------------------------------------------

    [Fact]
    public async Task Refreshing_returns_a_new_access_token_and_a_different_refresh_token()
    {
        var signed = await SignInAsync();

        var response = await RefreshAsync(signed.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var next = body.GetProperty("refreshToken").GetString()!;

        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();

        // Rotation is what makes theft detectable: if the token never changed,
        // a stolen one would work forever and look identical to the real one.
        next.Should().NotBe(signed.RefreshToken);
    }

    [Fact]
    public async Task The_replacement_stays_in_the_same_session()
    {
        var signed = await SignInAsync();

        await RefreshAsync(signed.RefreshToken);

        var sessions = await factory.WithDbContextAsync(db =>
            db.RefreshTokens.AsNoTracking().Select(t => t.SessionId).Distinct().ToListAsync());

        // One chain, so revoking the session revokes both.
        sessions.Should().ContainSingle();
    }

    [Fact]
    public async Task Refreshing_picks_up_a_role_granted_since_sign_in()
    {
        // Roles are re-read on refresh rather than carried through the session,
        // so a change takes effect within the access token's lifetime instead
        // of at the next sign-in.
        var signed = await SignInAsync();

        var userId = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.MobileOrEmail == Member).Select(u => u.Id).SingleAsync());

        var admin = factory.CreateClientAs(
            Guid.NewGuid(),
            await TenantIdAsync(),
            [Application.Security.Roles.SamaajAdmin],
            [Application.Security.PermissionKeys.AdminUsersManage]);

        var granted = await admin.PutAsJsonAsync(
            $"/v1/identity/admins/{userId}/roles/{Application.Security.Roles.ContentModerator}",
            new { granted = true });

        granted.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = await RefreshAsync(signed.RefreshToken);
        var body = await refreshed.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("roles").EnumerateArray().Select(r => r.GetString())
            .Should().Contain(Application.Security.Roles.ContentModerator);
    }

    // ---- Reuse detection --------------------------------------------------

    [Fact]
    public async Task Using_a_refresh_token_twice_is_refused()
    {
        var signed = await SignInAsync();

        (await RefreshAsync(signed.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await RefreshAsync(signed.RefreshToken);

        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reusing_a_refresh_token_kills_the_whole_session_including_the_live_one()
    {
        // The theft case. Someone copied the token, the real member used it
        // first, and the thief presents the copy. There is no way to tell which
        // party is which, so both are signed out - an inconvenience for the
        // member, and the end of the attacker's access.
        var signed = await SignInAsync();

        var refreshed = await RefreshAsync(signed.RefreshToken);
        var live = (await refreshed.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("refreshToken").GetString()!;

        await RefreshAsync(signed.RefreshToken);

        var afterReplay = await RefreshAsync(live);

        afterReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var reasons = await factory.WithDbContextAsync(db =>
            db.RefreshTokens.AsNoTracking().Select(t => t.RevokedReason).ToListAsync());

        reasons.Should().OnlyContain(r => r == SessionEndReason.ReuseDetected);
    }

    [Fact]
    public async Task A_reuse_revocation_survives_the_failing_request_that_detected_it()
    {
        // The refresh returns a failure, and TransactionBehavior rolls a failed
        // command back. Committing the revocations anyway is the entire point
        // of having detected the reuse; this is the same trap the failed-login
        // counter hit.
        var signed = await SignInAsync();

        await RefreshAsync(signed.RefreshToken);
        await RefreshAsync(signed.RefreshToken);

        var revoked = await factory.WithDbContextAsync(db =>
            db.RefreshTokens.AsNoTracking().CountAsync(t => t.RevokedAt != null));

        revoked.Should().Be(2);
    }

    // ---- Signing out ------------------------------------------------------

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var signed = await SignInAsync();

        var response = await SignOutAsync(signed.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(signed.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_twice_looks_exactly_like_signing_out_once()
    {
        // A count that distinguished them would say which tokens exist.
        var signed = await SignInAsync();

        (await SignOutAsync(signed.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SignOutAsync(signed.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_with_a_token_nobody_recognises_is_not_an_error()
    {
        var response = await SignOutAsync("not-a-real-token");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessionsEnded").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_the_other_devices_too()
    {
        var first = await SignInAsync();
        var second = await LoginAsync();

        await SignOutAsync(second.RefreshToken, everywhere: true);

        (await RefreshAsync(first.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_one_session_leaves_the_others_alone()
    {
        var first = await SignInAsync();
        var second = await LoginAsync();

        await SignOutAsync(second.RefreshToken);

        (await RefreshAsync(first.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Revocation from elsewhere ----------------------------------------

    [Fact]
    public async Task Erasing_an_account_ends_every_session_it_had()
    {
        var signed = await SignInAsync();
        var other = await LoginAsync();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", signed.AccessToken);

        (await client.PostAsJsonAsync("/v1/identity/me/erase", new { password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The access token outlives this by its remaining minutes and nothing
        // can withdraw it - but nothing can renew it either.
        (await RefreshAsync(signed.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(other.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var reasons = await factory.WithDbContextAsync(db =>
            db.RefreshTokens.AsNoTracking()
                .Where(t => t.RevokedAt != null)
                .Select(t => t.RevokedReason)
                .Distinct()
                .ToListAsync());

        reasons.Should().ContainSingle().Which.Should().Be(SessionEndReason.AccountErased);
    }

    [Fact]
    public async Task A_session_cannot_be_continued_once_its_Samaaj_is_deactivated()
    {
        // Deactivating a Samaaj could not previously reach anyone already
        // signed in. It still cannot reach their current access token, but the
        // session stops at the next refresh.
        var signed = await SignInAsync();
        var tenantId = await TenantIdAsync();

        var platformAdmin = factory.CreateClientWith(Application.Security.PermissionKeys.TenantManage);

        (await platformAdmin.PatchAsJsonAsync(
                $"/v1/identity/tenants/{tenantId}/status", new { status = "Inactive" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(signed.RefreshToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private Task<Guid> TenantIdAsync() =>
        factory.WithDbContextAsync(db =>
            db.Tenants.AsNoTracking().Where(t => t.Slug == _slug).Select(t => t.Id).SingleAsync());
}
