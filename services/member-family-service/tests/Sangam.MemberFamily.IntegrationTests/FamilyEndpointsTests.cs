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

public sealed class FamilyEndpointsTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Guid _head = Guid.NewGuid();
    private readonly Guid _joiner = Guid.NewGuid();
    private readonly Guid _stranger = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await SeedProfileAsync(_head, TenantA, "Ravi Shah");
        await SeedProfileAsync(_joiner, TenantA, "Meera Shah");
        await SeedProfileAsync(_stranger, TenantB, "Someone Else");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedProfileAsync(Guid id, Guid tenantId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        if (await db.MemberProfiles.IgnoreQueryFilters().AnyAsync(p => p.Id == id))
        {
            return;
        }

        db.MemberProfiles.Add(MemberProfile.FromRegistration(
            id, tenantId, name, $"{id:N}@example.com", DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
    }

    private HttpClient MemberClient(Guid userId, Guid tenantId) =>
        factory.CreateClientAs(userId, tenantId, ["Member"], ["Members.Read", "Family.Write"]);

    [Fact]
    public async Task Family_endpoints_need_a_token()
    {
        (await factory.CreateClient().GetAsync("/v1/families/mine"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_creates_a_family_and_becomes_its_head()
    {
        var response = await MemberClient(_head, TenantA).PostAsync("/v1/families", null);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("viewerIsHead").GetBoolean().Should().BeTrue();
        body.GetProperty("familyHeadMemberId").GetGuid().Should().Be(_head);
        body.GetProperty("familyCode").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_member_cannot_create_a_second_family()
    {
        var client = MemberClient(_head, TenantA);

        await client.PostAsync("/v1/families", null);

        (await client.PostAsync("/v1/families", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Joining_and_being_accepted_walks_the_whole_flow()
    {
        var headClient = MemberClient(_head, TenantA);
        var joinerClient = MemberClient(_joiner, TenantA);

        var created = await headClient.PostAsync("/v1/families", null);
        var family = await created.Content.ReadFromJsonAsync<JsonElement>();
        var familyId = family.GetProperty("id").GetGuid();
        var code = family.GetProperty("familyCode").GetString()!;

        var requested = await joinerClient.PostAsJsonAsync(
            "/v1/families/join-requests", new { familyCode = code, relationship = "Spouse" });

        requested.StatusCode.Should().Be(HttpStatusCode.OK);

        var requestBody = await requested.Content.ReadFromJsonAsync<JsonElement>();

        // The joiner is not the head, so they must not be handed the code that
        // would let them invite anyone else.
        requestBody.GetProperty("familyCode").ValueKind.Should().Be(JsonValueKind.Null);

        var requestId = requestBody.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("memberProfileId").GetGuid() == _joiner)
            .GetProperty("id").GetGuid();

        var decided = await headClient.PostAsJsonAsync(
            $"/v1/families/{familyId}/join-requests/{requestId}/decide", new { accept = true });

        decided.StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await joinerClient.GetFromJsonAsync<JsonElement>("/v1/families/mine");

        mine.GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("memberProfileId").GetGuid() == _joiner)
            .GetProperty("status").GetString().Should().Be("Active");
    }

    [Fact]
    public async Task Only_the_head_can_decide_a_join_request()
    {
        var headClient = MemberClient(_head, TenantA);
        var joinerClient = MemberClient(_joiner, TenantA);

        var created = await headClient.PostAsync("/v1/families", null);
        var family = await created.Content.ReadFromJsonAsync<JsonElement>();
        var familyId = family.GetProperty("id").GetGuid();
        var code = family.GetProperty("familyCode").GetString()!;

        var requested = await joinerClient.PostAsJsonAsync(
            "/v1/families/join-requests", new { familyCode = code, relationship = "Sibling" });

        var requestId = (await requested.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("members").EnumerateArray()
            .Single(m => m.GetProperty("memberProfileId").GetGuid() == _joiner)
            .GetProperty("id").GetGuid();

        // Deciding your own request would make the head's approval meaningless.
        var decided = await joinerClient.PostAsJsonAsync(
            $"/v1/families/{familyId}/join-requests/{requestId}/decide", new { accept = true });

        decided.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_family_code_from_another_Samaaj_admits_nobody()
    {
        var created = await MemberClient(_head, TenantA).PostAsync("/v1/families", null);
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("familyCode").GetString()!;

        var outsider = MemberClient(_stranger, TenantB);

        var response = await outsider.PostAsJsonAsync(
            "/v1/families/join-requests", new { familyCode = code, relationship = "Other" });

        // Reported as "no such family", not "wrong Samaaj": the difference
        // would confirm a code exists somewhere on the platform.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unknown_code_is_a_404()
    {
        var response = await MemberClient(_joiner, TenantA).PostAsJsonAsync(
            "/v1/families/join-requests", new { familyCode = "ZZZZ9999", relationship = "Other" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_with_no_family_is_told_so_rather_than_given_an_error()
    {
        var response = await MemberClient(_stranger, TenantB).GetAsync("/v1/families/mine");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("title").GetString().Should().Be("Family.None");
    }

    // ---- Taking back a request ----------------------------------------------
    //
    // A pending request counts as belonging to a household, so until this
    // existed a member waiting on a head who never answered could not join
    // anywhere else or create their own - indefinitely, with no way out that
    // did not run through somebody else.

    /// <summary>
    /// The dead end, end to end: ask, be ignored, and be unable to do anything
    /// about it - then withdraw and be free.
    /// </summary>
    [Fact]
    public async Task A_member_waiting_on_a_head_who_never_answers_can_take_it_back()
    {
        var head = MemberClient(_head, TenantA);
        var joiner = MemberClient(_joiner, TenantA);

        var created = await head.PostAsJsonAsync("/v1/families", new { });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("familyCode").GetString();

        (await joiner.PostAsJsonAsync(
            "/v1/families/join-requests", new { familyCode = code, relationship = "Sibling" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Stuck: cannot create a household of their own while one request stands.
        (await joiner.PostAsJsonAsync("/v1/families", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var withdrawn = await joiner.DeleteAsync("/v1/families/join-requests/mine");
        withdrawn.StatusCode.Should().Be(HttpStatusCode.OK);

        (await withdrawn.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("withdrawn").GetBoolean().Should().BeTrue();

        // And now free to have a household at all.
        (await joiner.PostAsJsonAsync("/v1/families", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Withdrawing_with_nothing_pending_is_success_and_changes_nothing()
    {
        var response = await MemberClient(_joiner, TenantA)
            .DeleteAsync("/v1/families/join-requests/mine");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("withdrawn").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// The head accepted while the member was deciding to withdraw. Quietly
    /// succeeding would take them out of a household they had just joined while
    /// telling them they had cancelled a request.
    /// </summary>
    [Fact]
    public async Task An_accepted_request_cannot_be_withdrawn()
    {
        var head = MemberClient(_head, TenantA);
        var joiner = MemberClient(_joiner, TenantA);

        var created = await head.PostAsJsonAsync("/v1/families", new { });
        var family = await created.Content.ReadFromJsonAsync<JsonElement>();
        var familyId = family.GetProperty("id").GetGuid();
        var code = family.GetProperty("familyCode").GetString();

        await joiner.PostAsJsonAsync(
            "/v1/families/join-requests", new { familyCode = code, relationship = "Sibling" });

        var mine = await head.GetFromJsonAsync<JsonElement>("/v1/families/mine");
        var requestId = mine.GetProperty("members").EnumerateArray()
            .First(m => m.GetProperty("status").GetString() == "PendingJoinRequest")
            .GetProperty("id").GetGuid();

        await head.PostAsJsonAsync(
            $"/v1/families/{familyId}/join-requests/{requestId}/decide", new { accept = true });

        (await joiner.DeleteAsync("/v1/families/join-requests/mine"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Still in the household, which is the point.
        (await joiner.GetAsync("/v1/families/mine")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Leaving a household ------------------------------------------------
    //
    // Joining one used to be permanent. An active membership counts as
    // belonging to a household, so a member could not create their own or ask
    // another - and nothing could remove them. Erasing your account was the only
    // way out, which is a right being used as a workaround for a missing
    // feature.

    /// <summary>
    /// The dead end, end to end: join, be stuck, leave, be free.
    /// </summary>
    [Fact]
    public async Task A_member_who_joined_a_household_can_leave_it_again()
    {
        var head = MemberClient(_head, TenantA);
        var joiner = MemberClient(_joiner, TenantA);

        var created = await head.PostAsJsonAsync("/v1/families", new { });
        var family = await created.Content.ReadFromJsonAsync<JsonElement>();
        var familyId = family.GetProperty("id").GetGuid();
        var code = family.GetProperty("familyCode").GetString();

        await joiner.PostAsJsonAsync(
            "/v1/families/join-requests", new { familyCode = code, relationship = "Sibling" });

        var mine = await head.GetFromJsonAsync<JsonElement>("/v1/families/mine");
        var requestId = mine.GetProperty("members").EnumerateArray()
            .First(m => m.GetProperty("status").GetString() == "PendingJoinRequest")
            .GetProperty("id").GetGuid();

        await head.PostAsJsonAsync(
            $"/v1/families/{familyId}/join-requests/{requestId}/decide", new { accept = true });

        // In, and therefore stuck: no second household, by design.
        (await joiner.PostAsJsonAsync("/v1/families", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var left = await joiner.DeleteAsync("/v1/families/mine/membership");
        left.StatusCode.Should().Be(HttpStatusCode.OK);

        (await left.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("left").GetBoolean().Should().BeTrue();

        // Free to have a household at all, which is what proves the membership
        // is gone rather than hidden.
        (await joiner.PostAsJsonAsync("/v1/families", new { }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Leaving_when_you_are_in_no_household_is_success_and_changes_nothing()
    {
        var response = await MemberClient(_joiner, TenantA)
            .DeleteAsync("/v1/families/mine/membership");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("left").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Asking is not joining, and the two calls say different things to whoever
    /// reads the audit log.
    /// </summary>
    [Fact]
    public async Task Somebody_who_has_only_asked_is_told_to_withdraw_instead()
    {
        var head = MemberClient(_head, TenantA);
        var joiner = MemberClient(_joiner, TenantA);

        var created = await head.PostAsJsonAsync("/v1/families", new { });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("familyCode").GetString();

        await joiner.PostAsJsonAsync(
            "/v1/families/join-requests", new { familyCode = code, relationship = "Sibling" });

        (await joiner.DeleteAsync("/v1/families/mine/membership"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// The one refusal, and it is about the children rather than about them.
    /// Nothing on this platform can remove a child record, so a household left
    /// with children and no members would stay that way permanently.
    /// </summary>
    [Fact]
    public async Task The_last_member_of_a_household_with_children_cannot_leave()
    {
        var head = MemberClient(_head, TenantA);

        await head.PostAsJsonAsync("/v1/families", new { });

        var notice = await head.GetFromJsonAsync<JsonElement>("/v1/children/data-notice");

        var addChild = await head.PostAsJsonAsync("/v1/children", new
        {
            fullName = "Aarav Shah",
            dateOfBirth = "2015-04-02",
            gender = "Male",
            parentalConsentGiven = true,
            noticeVersion = notice.GetProperty("version").GetString(),
        });

        addChild.StatusCode.Should().Be(HttpStatusCode.Created);

        var refused = await head.DeleteAsync("/v1/families/mine/membership");

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("children");
    }
}
