using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Signing in as production runs it: a CAPTCHA and a password, then a code on a
/// device the account does not trust - end to end, against a real database.
/// </summary>
/// <remarks>
/// The code is read off the outbox row this service hands the notification
/// pipeline, the only place it exists in plaintext. A CAPTCHA's answer exists
/// nowhere at all, so the tests that need a solvable one mint it the way the
/// service does and store it themselves; the endpoint that issues them is
/// tested for what it returns and what it keeps.
/// </remarks>
public sealed class SignInSecurityTests(GuardedIdentityTenantApiFactory factory)
    : IClassFixture<GuardedIdentityTenantApiFactory>, IAsyncLifetime
{
    private const string Password = "a-long-enough-password";
    private const string Member = "ravi@example.com";

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();

        var slug = (await factory.SeedActiveTenantAsync()).Slug;
        var notice = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/v1/identity/consent-notice");

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = slug,
            fullName = "Ravi Shah",
            mobileOrEmail = Member,
            password = Password,
            consentedPurposes = new[] { "Membership" },
            noticeVersion = notice.GetProperty("version").GetString(),
        })).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>A CAPTCHA whose answer this test knows, stored exactly as the service stores one.</summary>
    private async Task<(Guid Id, string Answer)> SolvableCaptchaAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var db = scope.ServiceProvider.GetRequiredService<Infrastructure.Persistence.IdentityTenantDbContext>();

        var (challenge, answer) = CaptchaChallenge.Issue(hasher.HashDeterministic, DateTimeOffset.UtcNow);
        db.CaptchaChallenges.Add(challenge);
        await db.SaveChangesAsync();

        return (challenge.Id, answer);
    }

    private async Task<HttpResponseMessage> LoginAsync(
        string password = Password, bool solve = true, string? deviceToken = null)
    {
        var (id, answer) = await SolvableCaptchaAsync();

        return await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = Member,
            password,
            captchaId = id,
            captchaAnswer = solve ? answer.ToLowerInvariant() : "WRONG",
            deviceToken,
        });
    }

    private async Task<string> LatestCodeAsync()
    {
        var payload = await factory.WithDbContextAsync(db => db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Topic == "identity.login-otp.requested.v1")
            .OrderByDescending(m => m.OccurredAt)
            .Select(m => m.Payload)
            .FirstAsync());

        return JsonDocument.Parse(payload).RootElement.GetProperty("code").GetString()!;
    }

    private Task<HttpResponseMessage> VerifyAsync(string challenge, string code, bool trust = false) =>
        factory.CreateClient().PostAsJsonAsync("/v1/identity/login/verify", new { challengeToken = challenge, code, trustDevice = trust });

    private static async Task<JsonElement> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    // ---- The CAPTCHA -----------------------------------------------------------

    [Fact]
    public async Task A_CAPTCHA_is_a_PNG_and_only_a_hash_of_its_answer_is_kept()
    {
        var response = await factory.CreateClient().PostAsync("/v1/identity/captcha", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await BodyOf(response);
        var png = Convert.FromBase64String(body.GetProperty("imagePng").GetString()!);
        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

        var id = body.GetProperty("captchaId").GetGuid();
        var stored = await factory.WithDbContextAsync(db => db.CaptchaChallenges.AsNoTracking().SingleAsync(c => c.Id == id));
        stored.AnswerHash.Should().StartWith("sha256:", "the answer itself is kept nowhere");
        stored.UsedAt.Should().BeNull();
    }

    [Fact]
    public void Every_character_a_CAPTCHA_may_use_can_be_drawn()
    {
        CaptchaChallenge.Alphabet.Where(c => !Infrastructure.Security.CaptchaRenderer.CanDraw(c))
            .Should().BeEmpty("an undrawable character is a CAPTCHA nobody can solve");
    }

    [Fact]
    public async Task Signing_in_without_the_CAPTCHA_or_with_a_wrong_answer_is_refused()
    {
        var bare = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new { mobileOrEmail = Member, password = Password });
        bare.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BodyOf(bare)).GetProperty("title").GetString().Should().Be("Auth.CaptchaFailed");

        var wrong = await LoginAsync(solve: false);
        (await BodyOf(wrong)).GetProperty("title").GetString().Should().Be("Auth.CaptchaFailed");
    }

    [Fact]
    public async Task A_CAPTCHA_is_spent_even_when_the_password_then_fails()
    {
        var (id, answer) = await SolvableCaptchaAsync();

        var first = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
            new { mobileOrEmail = Member, password = "not-the-password", captchaId = id, captchaAnswer = answer });
        (await BodyOf(first)).GetProperty("title").GetString().Should().Be("Auth.InvalidCredentials");

        // The same solve, again - the replay single use exists to stop.
        var replay = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login",
            new { mobileOrEmail = Member, password = Password, captchaId = id, captchaAnswer = answer });
        (await BodyOf(replay)).GetProperty("title").GetString().Should().Be("Auth.CaptchaFailed");
    }

    // ---- The second factor -------------------------------------------------------

    [Fact]
    public async Task A_password_alone_signs_nobody_in_and_the_code_then_does()
    {
        var step1 = await LoginAsync();
        step1.StatusCode.Should().Be(HttpStatusCode.OK);

        var challenge = await BodyOf(step1);
        challenge.GetProperty("codeRequired").GetBoolean().Should().BeTrue();
        challenge.GetProperty("session").ValueKind.Should().Be(JsonValueKind.Null);
        challenge.GetProperty("codeSentTo").GetString().Should().Be("ra***@example.com");

        var token = challenge.GetProperty("challengeToken").GetString()!;
        var code = await LatestCodeAsync();

        var step2 = await VerifyAsync(token, code);
        step2.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BodyOf(step2)).GetProperty("session").GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();

        // Spent: the same challenge and code sign nobody in twice.
        (await VerifyAsync(token, code)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_code_without_the_challenge_its_password_earned_is_worth_nothing()
    {
        await LoginAsync();
        var code = await LatestCodeAsync();

        var response = await VerifyAsync("made-up-challenge", code);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await BodyOf(response)).GetProperty("title").GetString().Should().Be("Auth.InvalidCode");
    }

    [Fact]
    public async Task Wrong_codes_lock_the_account_like_wrong_passwords()
    {
        var token = (await BodyOf(await LoginAsync())).GetProperty("challengeToken").GetString()!;
        var code = await LatestCodeAsync();
        var wrong = code == "999999" ? "000000" : "999999";

        for (var attempt = 0; attempt < User.MaxFailedAttempts; attempt++)
        {
            (await VerifyAsync(token, wrong)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var locked = await VerifyAsync(token, code);
        locked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BodyOf(locked)).GetProperty("title").GetString().Should().Be("Auth.LockedOut");
    }

    [Fact]
    public async Task A_trusted_device_skips_the_code_for_thirty_days_until_every_session_ends()
    {
        var token = (await BodyOf(await LoginAsync())).GetProperty("challengeToken").GetString()!;
        var signedIn = await BodyOf(await VerifyAsync(token, await LatestCodeAsync(), trust: true));

        var device = signedIn.GetProperty("deviceToken").GetString()!;
        signedIn.GetProperty("deviceTrustedUntil").GetDateTimeOffset()
            .Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));

        var again = await BodyOf(await LoginAsync(deviceToken: device));
        again.GetProperty("codeRequired").GetBoolean().Should().BeFalse();
        var session = again.GetProperty("session");

        // ...but never the password.
        (await BodyOf(await LoginAsync(password: "not-the-password", deviceToken: device)))
            .GetProperty("title").GetString().Should().Be("Auth.InvalidCredentials");

        // Signing out everywhere forgets the device too.
        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/logout",
            new { refreshToken = session.GetProperty("refreshToken").GetString(), everywhere = true }))
            .EnsureSuccessStatusCode();

        (await BodyOf(await LoginAsync(deviceToken: device))).GetProperty("codeRequired").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_resend_sends_a_new_code_no_sooner_than_thirty_seconds()
    {
        var token = (await BodyOf(await LoginAsync())).GetProperty("challengeToken").GetString()!;

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/login/resend", new { challengeToken = token }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var codes = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.CountAsync(m => m.Topic == "identity.login-otp.requested.v1"));
        codes.Should().Be(1, "the first code went out a moment ago");

        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/login/resend", new { challengeToken = "nothing" }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "an unknown challenge gets the same answer");
    }

    [Fact]
    public async Task The_retired_passwordless_routes_are_gone()
    {
        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/otp/request", new { mobileOrEmail = Member }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await factory.CreateClient().PostAsJsonAsync("/v1/identity/otp/login", new { mobileOrEmail = Member, code = "123456" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
