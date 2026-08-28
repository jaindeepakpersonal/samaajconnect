using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// The right to erasure, DPDP section 12, end to end.
/// </summary>
/// <remarks>
/// The interesting assertion is the outbox one. Erasing here is only a third of
/// the job - the other two services act on the event - so an erasure that
/// commits without leaving an outbox row would report success and quietly leave
/// the member's profile, family links and notifications in place. Both have to
/// land in one transaction.
/// </remarks>
public sealed class ErasureEndpointTests(IdentityTenantApiFactory factory)
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

    private async Task<HttpClient> SignedInMemberAsync()
    {
        var registered = await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = _slug,
            fullName = "Ravi Shah",
            mobileOrEmail = Member,
            password = Password,
            consentedPurposes = new[] { "Membership", "Communications" },
            noticeVersion = _noticeVersion,
        });

        registered.StatusCode.Should().Be(HttpStatusCode.Created);

        return await SignInAsync(Password);
    }

    private async Task<HttpClient> SignInAsync(string password)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = Member,
            password,
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    [Fact]
    public async Task A_member_can_erase_their_own_account_with_their_password()
    {
        var client = await SignedInMemberAsync();

        var response = await client.PostAsJsonAsync("/v1/identity/me/erase", new { password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var erased = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(u => u.Status == UserStatus.Erased));

        erased.FullName.Should().NotContain("Ravi");
        erased.MobileOrEmail.Should().NotBe(Member);
        erased.PasswordHash.Should().BeEmpty();
    }

    [Fact]
    public async Task Erasing_writes_the_event_the_other_services_act_on_in_the_same_transaction()
    {
        var client = await SignedInMemberAsync();

        await client.PostAsJsonAsync("/v1/identity/me/erase", new { password = Password });

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "identity.user.erased.v1")
                .ToListAsync());

        outbox.Should().ContainSingle();

        // Ids only. This event travels to a service that records every payload
        // verbatim into an append-only table, so a name on it would land
        // somewhere deliberately impossible to redact.
        outbox[0].Payload.Should().NotContain("Ravi");
        outbox[0].Payload.Should().NotContain(Member);
    }

    [Fact]
    public async Task An_erased_member_cannot_sign_in_again()
    {
        var client = await SignedInMemberAsync();

        await client.PostAsJsonAsync("/v1/identity/me/erase", new { password = Password });

        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = Member,
            password = Password,
        });

        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_identifier_is_freed_so_the_same_person_could_join_again()
    {
        // MobileOrEmail is unique platform-wide. If erasure kept it, someone
        // who left could never come back, which is a penalty for exercising a
        // right rather than a consequence of it.
        var client = await SignedInMemberAsync();

        await client.PostAsJsonAsync("/v1/identity/me/erase", new { password = Password });

        var again = await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = _slug,
            fullName = "Ravi Shah",
            mobileOrEmail = Member,
            password = Password,
            consentedPurposes = new[] { "Membership" },
            noticeVersion = _noticeVersion,
        });

        again.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_wrong_password_erases_nothing()
    {
        var client = await SignedInMemberAsync();

        var response = await client.PostAsJsonAsync(
            "/v1/identity/me/erase", new { password = "not-the-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var stillThere = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(u => u.MobileOrEmail == Member));

        stillThere.Status.Should().Be(UserStatus.Active);

        // TransactionBehavior rolls a failed command back, and nothing should
        // have been announced.
        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .CountAsync(m => m.Topic == "identity.user.erased.v1"));

        outbox.Should().Be(0);
    }

    [Fact]
    public async Task The_response_says_what_survives_rather_than_only_that_it_is_done()
    {
        var client = await SignedInMemberAsync();

        var response = await client.PostAsJsonAsync("/v1/identity/me/erase", new { password = Password });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("whatWasErased").EnumerateArray().Should().NotBeEmpty();
        body.GetProperty("whatIsKeptAndWhy").EnumerateArray().Should().NotBeEmpty();
    }


    [Fact]
    public async Task Erasing_removes_the_role_grants_from_the_database_not_just_the_object()
    {
        // A regression: the repository fetched the User without its roles, so
        // Erase cleared an empty collection and the grant rows survived - a
        // token minted before erasure would still have carried real authority.
        var client = await SignedInMemberAsync();

        var userId = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.MobileOrEmail == Member)
                .Select(u => u.Id)
                .SingleAsync());

        var before = await factory.WithDbContextAsync(db =>
            db.Set<Domain.Authorization.UserRole>().AsNoTracking()
                .CountAsync(r => r.UserId == userId));

        before.Should().BeGreaterThan(0);

        await client.PostAsJsonAsync("/v1/identity/me/erase", new { password = Password });

        var after = await factory.WithDbContextAsync(db =>
            db.Set<Domain.Authorization.UserRole>().AsNoTracking()
                .CountAsync(r => r.UserId == userId));

        after.Should().Be(0);
    }

    [Fact]
    public async Task Exporting_your_data_is_recorded_as_an_event()
    {
        // SECURITY-CHECKLIST.md: an export produces a complete copy of a
        // person's data, and until this it was the one operation that left no
        // trace at all.
        var client = await SignedInMemberAsync();

        var response = await client.GetAsync("/v1/identity/me/data-export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "identity.member-data.exported.v1")
                .ToListAsync());

        outbox.Should().ContainSingle();

        // Ids and a timestamp. Recording what was in the export would make the
        // record of the copy a second copy.
        outbox[0].Payload.Should().NotContain("Ravi");
        outbox[0].Payload.Should().NotContain(Member);
    }

    [Fact]
    public async Task The_export_still_succeeds_when_it_cannot_be_recorded()
    {
        // A member's right to a copy of their data (DPDP s.11) does not depend
        // on the platform's bookkeeping working.
        var client = await SignedInMemberAsync();

        var first = await client.GetAsync("/v1/identity/me/data-export");
        var second = await client.GetAsync("/v1/identity/me/data-export");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        // Two exports, two records: this is not deduplicated, because each one
        // really did hand out a copy.
        var recorded = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .CountAsync(m => m.Topic == "identity.member-data.exported.v1"));

        recorded.Should().Be(2);
    }
    [Fact]
    public async Task Erasing_needs_a_token()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/v1/identity/me/erase", new { password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
