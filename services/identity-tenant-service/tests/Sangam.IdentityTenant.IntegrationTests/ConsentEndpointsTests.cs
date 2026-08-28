using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// The DPDP surface. See docs/product/DPDP-COMPLIANCE.md for which obligation
/// each of these is standing in for.
/// </summary>
public sealed class ConsentEndpointsTests(IdentityTenantApiFactory factory)
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

    /// <summary>Consent rows for the member these tests register.</summary>
    private Task<List<Domain.Consents.ConsentRecord>> ConsentRowsAsync() =>
        factory.WithDbContextAsync(async db =>
        {
            var userId = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.MobileOrEmail == Member)
                .Select(u => u.Id)
                .SingleAsync();

            return await db.ConsentRecords.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.UserId == userId)
                .ToListAsync();
        });

    private Task<HttpResponseMessage> RegisterAsync(
        IReadOnlyCollection<string>? purposes = null, string? noticeVersion = null) =>
        factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = _slug,
            fullName = "Ravi Shah",
            mobileOrEmail = Member,
            password = Password,
            consentedPurposes = purposes ?? new[] { "Membership", "Communications" },
            noticeVersion = noticeVersion ?? _noticeVersion,
        });

    private async Task<HttpClient> SignedInMemberAsync()
    {
        (await RegisterAsync()).StatusCode.Should().Be(HttpStatusCode.Created);

        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = Member,
            password = Password,
        });

        var token = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    [Fact]
    public async Task The_notice_is_public_because_it_is_shown_before_anyone_has_an_account()
    {
        var notice = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/v1/identity/consent-notice");

        notice.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();

        var items = notice.GetProperty("items").EnumerateArray().ToList();

        items.Should().NotBeEmpty();
        items.Should().Contain(item => item.GetProperty("purpose").GetString() == "Membership");
    }

    [Fact]
    public async Task The_notice_marks_which_purposes_are_required()
    {
        var notice = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/v1/identity/consent-notice");

        var membership = notice.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("purpose").GetString() == "Membership");

        var communications = notice.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("purpose").GetString() == "Communications");

        // Consent conditional on service is only valid where the service truly
        // cannot be given without it, so the required list stays short.
        membership.GetProperty("required").GetBoolean().Should().BeTrue();
        communications.GetProperty("required").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Registration_without_the_required_consent_is_refused()
    {
        var response = await RegisterAsync(purposes: ["Communications"]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registration_without_a_notice_version_is_refused()
    {
        // A consent record that cannot say what the person was shown is not
        // worth much under section 6(7).
        var response = await RegisterAsync(noticeVersion: "");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unknown_purpose_is_refused()
    {
        var response = await RegisterAsync(purposes: ["Membership", "SellToBrokers"]);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registering_records_one_consent_row_per_purpose_with_the_version_shown()
    {
        (await RegisterAsync()).StatusCode.Should().Be(HttpStatusCode.Created);

        var records = await ConsentRowsAsync();

        records.Should().HaveCount(2);
        records.Should().OnlyContain(r => r.NoticeVersion == _noticeVersion);
        records.Should().OnlyContain(r => r.Source == "Registration");
    }

    [Fact]
    public async Task Consent_is_announced_so_the_audit_log_records_it()
    {
        await RegisterAsync();

        var topics = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().Select(m => m.Topic).ToListAsync());

        topics.Should().Contain("identity.consent.recorded.v1");
    }

    [Fact]
    public async Task A_member_sees_the_current_state_of_each_consent_and_its_history()
    {
        var member = await SignedInMemberAsync();

        var export = await member.GetFromJsonAsync<JsonElement>("/v1/identity/me/data-export");

        export.GetProperty("currentConsents").EnumerateArray()
            .Select(c => c.GetProperty("purpose").GetString())
            .Should().Contain("Membership").And.Contain("Communications");

        export.GetProperty("consentHistory").EnumerateArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task Withdrawing_is_one_call_and_takes_effect_immediately()
    {
        var member = await SignedInMemberAsync();

        var response = await member.PostAsync("/v1/identity/me/consents/Communications/withdraw", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var state = await response.Content.ReadFromJsonAsync<JsonElement>();

        state.EnumerateArray()
            .Single(s => s.GetProperty("purpose").GetString() == "Communications")
            .GetProperty("granted").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Withdrawing_appends_rather_than_rewriting_the_record()
    {
        var member = await SignedInMemberAsync();

        await member.PostAsync("/v1/identity/me/consents/Communications/withdraw", null);

        var records = await ConsentRowsAsync();

        // Two grants at registration plus one withdrawal. Section 6(7) needs
        // the consent that was relied on to remain producible.
        records.Should().HaveCount(3);
        records.Should().Contain(r => r.Action == Domain.Consents.ConsentAction.Granted);
        records.Should().Contain(r => r.Action == Domain.Consents.ConsentAction.Withdrawn);
    }

    [Fact]
    public async Task The_consent_the_membership_rests_on_cannot_be_withdrawn_piecemeal()
    {
        var member = await SignedInMemberAsync();

        var response = await member.PostAsync("/v1/identity/me/consents/Membership/withdraw", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // And the message says what to do instead, rather than just refusing.
        problem.GetProperty("detail").GetString().Should().Contain("erase");
    }

    [Fact]
    public async Task Withdrawing_an_unknown_purpose_is_a_404()
    {
        var member = await SignedInMemberAsync();

        (await member.PostAsync("/v1/identity/me/consents/Nonsense/withdraw", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_export_needs_a_token()
    {
        (await factory.CreateClient().GetAsync("/v1/identity/me/data-export"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_export_covers_the_account_and_says_what_the_data_is_used_for()
    {
        var member = await SignedInMemberAsync();

        var export = await member.GetFromJsonAsync<JsonElement>("/v1/identity/me/data-export");

        export.GetProperty("account").GetProperty("mobileOrEmail").GetString().Should().Be(Member);
        export.GetProperty("account").GetProperty("roles").EnumerateArray()
            .Select(r => r.GetString()).Should().Contain("Member");

        // Section 11 asks for the processing activities, not just the data.
        export.GetProperty("processingPurposes").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_export_never_contains_the_password_hash()
    {
        var member = await SignedInMemberAsync();

        var body = await member.GetStringAsync("/v1/identity/me/data-export");

        // A credential is data about the person only in the sense that a lock
        // is about a key. Exporting it in the name of transparency would be a
        // way of handing one out.
        body.Should().NotContain("passwordHash");
        body.Should().NotContain("pbkdf2");
    }

    [Fact]
    public async Task The_export_says_what_it_does_not_cover()
    {
        var member = await SignedInMemberAsync();

        var export = await member.GetFromJsonAsync<JsonElement>("/v1/identity/me/data-export");

        // Per-service by design, so the export has to be honest about being a
        // part rather than the whole.
        export.GetProperty("heldElsewhere").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain(e => e!.Contains("member-family-service"));
    }
}
