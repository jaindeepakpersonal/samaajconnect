using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// The admin-approved conversion flow end to end: the head adds a child, the
/// child turns 18, the head asks, and a Samaaj admin decides.
/// </summary>
public sealed class ChildEndpointsTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private readonly Guid _head = Guid.NewGuid();
    private readonly Guid _otherMember = Guid.NewGuid();
    private readonly string _run = Guid.NewGuid().ToString("N")[..6];

    public async Task InitializeAsync()
    {
        await SeedProfileAsync(_head, "Ravi Shah " + _run);
        await SeedProfileAsync(_otherMember, "Meera Shah " + _run);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedProfileAsync(Guid id, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        if (await db.MemberProfiles.IgnoreQueryFilters().AnyAsync(p => p.Id == id))
        {
            return;
        }

        db.MemberProfiles.Add(MemberProfile.FromRegistration(
            id, TenantA, name, id.ToString("N") + "@example.com", DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
    }

    private HttpClient MemberClient(Guid userId) =>
        factory.CreateClientAs(userId, TenantA, ["Member"], ["Members.Read", "Family.Write"]);

    private HttpClient AdminClient() =>
        factory.CreateClientAs(
            Guid.NewGuid(), TenantA, ["SamaajAdmin"], ["Family.ApproveConversion"]);

    /// <summary>Creates the head's family and returns the client that heads it.</summary>
    private async Task<HttpClient> HeadWithFamilyAsync()
    {
        var client = MemberClient(_head);

        var created = await client.PostAsync("/v1/families", null);
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        return client;
    }

    private static object NewChild(DateOnly dateOfBirth, bool consent = true) => new
    {
        fullName = "Aarav Jain",
        dateOfBirth = dateOfBirth.ToString("yyyy-MM-dd"),
        gender = "Male",
        photoUrl = (string?)null,
        // DPDP section 9: a child record cannot be created without recorded
        // parental consent, so every caller has to attest.
        parentalConsentGiven = consent,
        noticeVersion = Domain.Children.ChildDataNotice.CurrentVersion,
    };

    private static DateOnly Aged(int years) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-years);

    [Fact]
    public async Task Child_endpoints_need_a_token()
    {
        (await factory.CreateClient().GetAsync("/v1/children"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_family_head_adds_a_child()
    {
        var head = await HeadWithFamilyAsync();

        var response = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(10)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var child = await response.Content.ReadFromJsonAsync<JsonElement>();

        child.GetProperty("fullName").GetString().Should().Be("Aarav Jain");
        child.GetProperty("status").GetString().Should().Be("Minor");
        child.GetProperty("age").GetInt32().Should().Be(10);
        child.GetProperty("isEligibleForConversion").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_member_with_no_family_is_told_to_make_one_first()
    {
        var response = await MemberClient(_otherMember)
            .PostAsJsonAsync("/v1/children", NewChild(Aged(10)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("title").GetString().Should().Be("Family.None");
    }

    [Fact]
    public async Task A_future_date_of_birth_is_a_validation_problem()
    {
        var head = await HeadWithFamilyAsync();

        var response = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_child_under_eighteen_cannot_be_converted()
    {
        var head = await HeadWithFamilyAsync();

        var created = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(17)));
        var childId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await head.PostAsJsonAsync(
            $"/v1/children/{childId}/conversion", new { mobileOrEmail = "aarav@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("title").GetString().Should().Be("Child.NotEligible");
    }

    [Fact]
    public async Task The_whole_admin_approved_conversion_flow_works()
    {
        var head = await HeadWithFamilyAsync();

        var created = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(18)));
        var childId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var requested = await head.PostAsJsonAsync(
            $"/v1/children/{childId}/conversion", new { mobileOrEmail = "aarav@example.com" });

        requested.StatusCode.Should().Be(HttpStatusCode.OK);

        var request = await requested.Content.ReadFromJsonAsync<JsonElement>();
        request.GetProperty("status").GetString().Should().Be("Pending");

        var requestId = request.GetProperty("id").GetGuid();

        // The head cannot approve their own request - that is the whole point
        // of the admin-approved decision.
        var selfApproval = await head.PostAsJsonAsync(
            $"/v1/children/conversion-requests/{requestId}/decide", new { approve = true });

        selfApproval.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = AdminClient();

        var queue = await admin.GetFromJsonAsync<JsonElement>("/v1/children/conversion-requests");
        queue.EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().Contain(requestId);

        var decided = await admin.PostAsJsonAsync(
            $"/v1/children/conversion-requests/{requestId}/decide",
            new { approve = true, note = "Verified in person" });

        decided.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await decided.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Approved");

        // The approval is announced for identity-tenant-service to create the
        // login; it does not create one here.
        var topics = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().Select(m => m.Topic).ToListAsync());

        topics.Should().Contain("members.child-conversion.approved.v1");
    }

    [Fact]
    public async Task An_approved_conversion_does_not_yet_mark_the_child_converted()
    {
        var head = await HeadWithFamilyAsync();

        var created = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(19)));
        var childId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var requested = await head.PostAsJsonAsync(
            $"/v1/children/{childId}/conversion", new { mobileOrEmail = "aarav2@example.com" });

        var requestId = (await requested.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        await AdminClient().PostAsJsonAsync(
            $"/v1/children/conversion-requests/{requestId}/decide", new { approve = true });

        var children = await head.GetFromJsonAsync<JsonElement>("/v1/children");

        // Still Minor: the login does not exist until identity has created it,
        // and a child record claiming an account nobody can sign in to would be
        // worse than one that lags by a second.
        children.EnumerateArray()
            .Single(c => c.GetProperty("id").GetGuid() == childId)
            .GetProperty("status").GetString().Should().Be("Minor");
    }

    [Fact]
    public async Task A_second_request_while_one_is_pending_is_refused()
    {
        var head = await HeadWithFamilyAsync();

        var created = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(20)));
        var childId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await head.PostAsJsonAsync(
            $"/v1/children/{childId}/conversion", new { mobileOrEmail = "aarav3@example.com" });

        var second = await head.PostAsJsonAsync(
            $"/v1/children/{childId}/conversion", new { mobileOrEmail = "aarav3@example.com" });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_member_cannot_read_the_conversion_queue()
    {
        var response = await MemberClient(_head).GetAsync("/v1/children/conversion-requests");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_without_the_approve_permission_cannot_decide()
    {
        var admin = factory.CreateClientAs(Guid.NewGuid(), TenantA, ["SamaajAdmin"], []);

        var response = await admin.PostAsJsonAsync(
            $"/v1/children/conversion-requests/{Guid.NewGuid()}/decide", new { approve = true });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_of_another_family_cannot_request_a_conversion()
    {
        var head = await HeadWithFamilyAsync();

        var created = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(21)));
        var childId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var stranger = MemberClient(_otherMember);

        var response = await stranger.PostAsJsonAsync(
            $"/v1/children/{childId}/conversion", new { mobileOrEmail = "someone@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_malformed_identifier_is_refused_before_an_admin_ever_sees_it()
    {
        var head = await HeadWithFamilyAsync();

        var created = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(22)));
        var childId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await head.PostAsJsonAsync(
            $"/v1/children/{childId}/conversion", new { mobileOrEmail = "not-an-identifier" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_child_record_cannot_be_created_without_parental_consent()
    {
        var head = await HeadWithFamilyAsync();

        // DPDP s.9: the consent is the basis on which this data may be held at
        // all, so the record should not be creatable without it.
        var response = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(10), consent: false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_consent_is_recorded_on_the_child_with_what_was_agreed()
    {
        var head = await HeadWithFamilyAsync();

        var created = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(10)));

        var child = await created.Content.ReadFromJsonAsync<JsonElement>();
        var consent = child.GetProperty("parentalConsent");

        consent.GetProperty("givenByMemberId").GetGuid().Should().Be(_head);
        consent.GetProperty("noticeVersion").GetString()
            .Should().Be(Domain.Children.ChildDataNotice.CurrentVersion);

        // Stored verbatim, so "what did they agree?" does not need answering
        // from source control.
        consent.GetProperty("attestation").GetString().Should().Contain("parent or lawful guardian");
    }

    [Fact]
    public async Task The_child_data_notice_is_available_to_show_before_asking()
    {
        var head = MemberClient(_head);

        var notice = await head.GetFromJsonAsync<JsonElement>("/v1/children/data-notice");

        notice.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
        notice.GetProperty("summary").GetString().Should().Contain("do not track children");
    }

    [Fact]
    public async Task The_data_export_covers_the_household_and_says_what_it_does_not()
    {
        var head = await HeadWithFamilyAsync();
        await head.PostAsJsonAsync("/v1/children", NewChild(Aged(9)));

        var export = await head.GetFromJsonAsync<JsonElement>("/v1/members/me/data-export");

        export.GetProperty("profile").GetProperty("fullName").GetString()
            .Should().Be("Ravi Shah " + _run);

        export.GetProperty("children").EnumerateArray().Should().ContainSingle();
        export.GetProperty("family").GetProperty("viewerIsHead").GetBoolean().Should().BeTrue();

        export.GetProperty("heldElsewhere").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain(e => e!.Contains("identity-tenant-service"));
    }

    [Fact]
    public async Task The_export_of_a_member_with_no_family_is_empty_rather_than_an_error()
    {
        var export = await MemberClient(_otherMember)
            .GetFromJsonAsync<JsonElement>("/v1/members/me/data-export");

        export.GetProperty("children").EnumerateArray().Should().BeEmpty();
        export.GetProperty("family").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// A decision note longer than the column holds is refused as a bad
    /// request, not as a server error.
    /// </summary>
    /// <remarks>
    /// `DecideChildConversionCommand` had no validator at all - one of the
    /// three requests on the platform carrying free text with nothing checking
    /// it - while `DecisionNote` is `HasMaxLength(1000)`. Postgres refuses a
    /// longer value with SQLSTATE 22001, `UnhandledExceptionBehavior` turns
    /// that into a generic failure, and an administrator who wrote a long note
    /// is told only that something went wrong.
    ///
    /// Root CLAUDE.md §4.3 asks for one validator per command for exactly this
    /// reason: `ValidationBehavior` runs the validators that exist, so a
    /// command with none has no input validation whatsoever.
    /// </remarks>
    [Fact]
    public async Task A_decision_note_longer_than_the_column_is_a_bad_request()
    {
        var head = await HeadWithFamilyAsync();

        var created = await head.PostAsJsonAsync("/v1/children", NewChild(Aged(18)));
        var childId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var requested = await head.PostAsJsonAsync(
            $"/v1/children/{childId}/conversion", new { mobileOrEmail = "long-note@example.com" });

        var requestId = (await requested.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetGuid();

        var decided = await AdminClient().PostAsJsonAsync(
            $"/v1/children/conversion-requests/{requestId}/decide",
            new { approve = false, note = new string('x', 1001) });

        decided.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
