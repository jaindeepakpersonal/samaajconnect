using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Users;
using Sangam.IdentityTenant.Infrastructure.Persistence;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// The tail of the adult-child conversion flow: an admin issues a one-time
/// code, the new member redeems it and can then sign in.
/// </summary>
public sealed class ActivationEndpointsTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string PendingUrl = "/v1/identity/activations/pending";
    private const string RedeemUrl = "/v1/identity/activations/redeem";
    private const string Password = "a-long-enough-password";

    private Guid _tenantId;
    private string _slug = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();

        var tenant = await factory.SeedActiveTenantAsync();
        _tenantId = tenant.Id;
        _slug = tenant.Slug;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AdminClient() =>
        factory.CreateClientAs(
            Guid.NewGuid(), _tenantId, [Roles.SamaajAdmin], [PermissionKeys.AdminUsersManage]);

    /// <summary>
    /// Creates the account the conversion consumer would have created. Doing it
    /// directly keeps these tests about activation rather than about Kafka,
    /// which the consumer's own test already covers.
    /// </summary>
    private async Task<Guid> SeedPendingAccountAsync(string identifier = "aarav@example.com")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();

        var user = User.CreateFromChildConversion(
            _tenantId,
            identifier,
            "Aarav Jain",
            Guid.NewGuid(),
            AuthorizationCatalog.RoleIds.Member,
            DateTimeOffset.UtcNow);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    private async Task<string> IssueCodeAsync(Guid userId)
    {
        var response = await AdminClient().PostAsync($"/v1/identity/activations/{userId}/code", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task The_pending_list_needs_an_admin()
    {
        (await factory.CreateClient().GetAsync(PendingUrl))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var member = factory.CreateClientAs(Guid.NewGuid(), _tenantId, [Roles.Member], []);

        (await member.GetAsync(PendingUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_account_awaiting_activation_appears_on_the_pending_list()
    {
        var userId = await SeedPendingAccountAsync();

        var pending = await AdminClient().GetFromJsonAsync<JsonElement>(PendingUrl);

        var entry = pending.EnumerateArray().Single(e => e.GetProperty("userId").GetGuid() == userId);

        entry.GetProperty("fullName").GetString().Should().Be("Aarav Jain");
        entry.GetProperty("hasUsableCode").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task The_pending_list_never_shows_a_code()
    {
        var userId = await SeedPendingAccountAsync();
        var code = await IssueCodeAsync(userId);

        var body = await AdminClient().GetStringAsync(PendingUrl);

        // Stored as a hash and unshowable by construction; asserting it anyway,
        // because "just add the code to the list" is a plausible future request.
        body.Should().NotContain(code);
        body.Should().Contain("\"hasUsableCode\":true");
    }

    [Fact]
    public async Task Issuing_a_code_returns_it_once()
    {
        var userId = await SeedPendingAccountAsync();

        var response = await AdminClient().PostAsync($"/v1/identity/activations/{userId}/code", null);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("code").GetString().Should().MatchRegex("^[A-HJ-NP-Z2-9]{10}$");
        body.GetProperty("mobileOrEmail").GetString().Should().Be("aarav@example.com");
    }

    [Fact]
    public async Task A_code_cannot_be_issued_for_an_account_that_is_already_active()
    {
        var admin = AdminClient();

        var registered = await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = _slug,
            fullName = "Ravi Shah",
            mobileOrEmail = "ravi@example.com",
            password = Password,
            consentedPurposes = new[] { "Membership" },
            noticeVersion = Domain.Consents.ConsentNotice.CurrentVersion,
        });

        var userId = (await registered.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("userId").GetGuid();

        var response = await admin.PostAsync($"/v1/identity/activations/{userId}/code", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Redeeming_a_code_activates_the_account_and_lets_the_member_sign_in()
    {
        var userId = await SeedPendingAccountAsync();
        var code = await IssueCodeAsync(userId);

        var redeemed = await factory.CreateClient().PostAsJsonAsync(RedeemUrl, new
        {
            mobileOrEmail = "aarav@example.com",
            code,
            password = Password,
        });

        redeemed.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await redeemed.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tenantSlug").GetString().Should().Be(_slug);

        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = "aarav@example.com",
            password = Password,
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Activation_announces_itself_so_member_family_can_close_the_loop()
    {
        var userId = await SeedPendingAccountAsync();
        var code = await IssueCodeAsync(userId);

        await factory.CreateClient().PostAsJsonAsync(RedeemUrl, new
        {
            mobileOrEmail = "aarav@example.com",
            code,
            password = Password,
        });

        var topics = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().Select(m => m.Topic).ToListAsync());

        topics.Should().Contain("identity.child-conversion.completed.v1");
    }

    [Fact]
    public async Task A_pending_account_cannot_be_signed_into_before_activation()
    {
        await SeedPendingAccountAsync();

        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = "aarav@example.com",
            password = Password,
        });

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_code_cannot_be_redeemed_twice()
    {
        var userId = await SeedPendingAccountAsync();
        var code = await IssueCodeAsync(userId);

        var payload = new { mobileOrEmail = "aarav@example.com", code, password = Password };

        (await factory.CreateClient().PostAsJsonAsync(RedeemUrl, payload))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await factory.CreateClient().PostAsJsonAsync(RedeemUrl, payload))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Re_issuing_invalidates_the_previous_code()
    {
        var userId = await SeedPendingAccountAsync();
        var first = await IssueCodeAsync(userId);
        var second = await IssueCodeAsync(userId);

        first.Should().NotBe(second);

        var withOld = await factory.CreateClient().PostAsJsonAsync(RedeemUrl, new
        {
            mobileOrEmail = "aarav@example.com",
            code = first,
            password = Password,
        });

        withOld.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Five_wrong_guesses_kill_the_code_and_the_right_one_stops_working()
    {
        var userId = await SeedPendingAccountAsync();
        var code = await IssueCodeAsync(userId);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await factory.CreateClient().PostAsJsonAsync(RedeemUrl, new
            {
                mobileOrEmail = "aarav@example.com",
                code = "WRONGCODE9",
                password = Password,
            });
        }

        // The counter had to survive the rollback of each failing command,
        // which is why IFailedActivationRecorder writes on its own connection.
        var withRealCode = await factory.CreateClient().PostAsJsonAsync(RedeemUrl, new
        {
            mobileOrEmail = "aarav@example.com",
            code,
            password = Password,
        });

        withRealCode.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Every_way_of_failing_looks_the_same_from_outside()
    {
        await SeedPendingAccountAsync();

        var wrongCode = await factory.CreateClient().PostAsJsonAsync(RedeemUrl, new
        {
            mobileOrEmail = "aarav@example.com",
            code = "WRONGCODE9",
            password = Password,
        });

        var noSuchAccount = await factory.CreateClient().PostAsJsonAsync(RedeemUrl, new
        {
            mobileOrEmail = "nobody@example.com",
            code = "WRONGCODE9",
            password = Password,
        });

        // Otherwise a list of identifiers could be sorted into those
        // mid-conversion and those not.
        wrongCode.StatusCode.Should().Be(noSuchAccount.StatusCode);

        var first = (await wrongCode.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("title").GetString();
        var second = (await noSuchAccount.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("title").GetString();

        first.Should().Be(second);
    }

    [Fact]
    public async Task A_weak_first_password_is_refused()
    {
        var userId = await SeedPendingAccountAsync();
        var code = await IssueCodeAsync(userId);

        var response = await factory.CreateClient().PostAsJsonAsync(RedeemUrl, new
        {
            mobileOrEmail = "aarav@example.com",
            code,
            password = "short",
        });

        // A converted child's first password is a real password, not a weaker
        // one because an admin vouched for them.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
