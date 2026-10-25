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

public sealed class MemberEndpointsTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    // Locality is a master-data-service pick now, not free text - these stand
    // in for "Udaipur" and "Pune" for the exact-match search tests.
    private static readonly Guid UdaipurLocalityId = Guid.NewGuid();
    private static readonly Guid PuneLocalityId = Guid.NewGuid();

    private readonly Guid _ravi = Guid.NewGuid();
    private readonly Guid _meera = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();
    private readonly Guid _puneMember = Guid.NewGuid();

    /// <summary>
    /// Suffix making this test method's seeded members distinguishable. The
    /// class fixture shares one database across the class, so without it every
    /// test would find every other test's Meera Shah.
    /// </summary>
    private readonly string _run = Guid.NewGuid().ToString("N")[..6];

    public async Task InitializeAsync()
    {
        await SeedAsync(_ravi, TenantA, Named("Ravi Shah"), UdaipurLocalityId);
        await SeedAsync(_meera, TenantA, Named("Meera Shah"), UdaipurLocalityId);
        await SeedAsync(_outsider, TenantB, Named("Someone Else"), PuneLocalityId);

        // Same Samaaj as Ravi and Meera, different locality - what an Area
        // Admin restricted to Udaipur must not see.
        await SeedAsync(_puneMember, TenantA, Named("Pune Member"), PuneLocalityId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync(Guid id, Guid tenantId, string name, Guid? localityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        if (await db.MemberProfiles.IgnoreQueryFilters().AnyAsync(p => p.Id == id))
        {
            return;
        }

        var contact = id.ToString("N") + "@example.com";

        var profile = MemberProfile.FromRegistration(
            id, tenantId, name, contact, DateTimeOffset.UtcNow);

        profile.Update(
            name,
            dateOfBirth: new DateOnly(1990, 1, 1),
            gender: Gender.Unspecified,
            mobile: "+919812345678",
            email: contact,
            gautraId: null,
            professionId: null,
            qualificationId: null,
            permanentAddress: new ProfileAddress("An address", CityId: null, localityId),
            temporaryAddress: null,
            privacy: new FieldPrivacy(
                PrivacyLevel.SamaajOnly,
                PrivacyLevel.Private,
                PrivacyLevel.Private,
                PrivacyLevel.SamaajOnly,
                PrivacyLevel.Private,
                PrivacyLevel.SamaajOnly),
            isListedInDirectory: true,
            updatedAt: DateTimeOffset.UtcNow,
            updatedBy: Guid.NewGuid());

        db.MemberProfiles.Add(profile);
        await db.SaveChangesAsync();
    }

    private HttpClient MemberClient(Guid userId, Guid tenantId) =>
        factory.CreateClientAs(userId, tenantId, ["Member"], ["Members.Read"]);

    private HttpClient AdminClient(Guid tenantId) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId, ["SamaajAdmin"], ["Members.Read", "Members.Write"]);

    /// <summary>
    /// "Member" alongside "AreaAdmin", exactly as a real token is minted -
    /// every admin role is granted alongside Member (User.Invite), and the
    /// read queries' own role gate relies on that rather than naming every
    /// admin role individually.
    /// </summary>
    private HttpClient AreaAdminClient(Guid tenantId, Guid localityId) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId, ["Member", "AreaAdmin"], ["Members.Read", "Members.Write"], localityId);

    private string Named(string name) => name + " " + _run;

    private static object PrivacyAll(string level) => new
    {
        mobile = level,
        email = level,
        address = level,
        profession = level,
        dateOfBirth = level,
        qualification = level,
    };

    /// <summary>
    /// A permanent address with nothing set - `UpdateProfileCommand` requires
    /// the object itself (there is no state for "no address at all", unlike
    /// the temporary one), so every PATCH body in this file needs one.
    /// </summary>
    private static object BlankAddress() => new { line = (string?)null, cityId = (Guid?)null, localityId = (Guid?)null };

    [Fact]
    public async Task The_directory_needs_a_token()
    {
        (await factory.CreateClient().GetAsync("/v1/members"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_without_Members_Read_is_refused()
    {
        var client = factory.CreateClientAs(_ravi, TenantA, ["Member"], []);

        (await client.GetAsync("/v1/members")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_directory_shows_only_this_Samaaj()
    {
        var directory = await MemberClient(_ravi, TenantA).GetFromJsonAsync<JsonElement>("/v1/members");

        var names = directory.GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("fullName").GetString())
            .ToList();

        names.Should().Contain(Named("Meera Shah"));
        names.Should().NotContain(Named("Someone Else"));
    }

    [Fact]
    public async Task Another_members_private_fields_come_back_null()
    {
        var directory = await MemberClient(_ravi, TenantA).GetFromJsonAsync<JsonElement>("/v1/members");

        var meera = directory.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == _meera);

        meera.GetProperty("mobile").GetString().Should().NotBeNullOrEmpty();
        meera.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
        meera.GetProperty("permanentAddress").GetProperty("line").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_Samaaj_admin_sees_the_private_fields_they_may_need_to_correct()
    {
        var directory = await AdminClient(TenantA).GetFromJsonAsync<JsonElement>("/v1/members");

        var meera = directory.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == _meera);

        meera.GetProperty("email").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Searching_matches_on_name()
    {
        var directory = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>("/v1/members?term=" + Uri.EscapeDataString(Named("Meera Shah")));

        directory.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("fullName").GetString().Should().Be(Named("Meera Shah"));
    }

    [Fact]
    public async Task Searching_does_not_match_on_a_private_contact_detail()
    {
        // Otherwise a private number could be confirmed one guess at a time,
        // whatever the field's privacy level said afterwards.
        var directory = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>("/v1/members?term=919812345678");

        directory.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Searching_by_locality_is_an_exact_match_not_a_substring()
    {
        // Locality moved from free text to a master-data-service pick, so
        // there is no longer a string to search toward - only a value to
        // filter on. The largest page: every test in this class seeds another
        // Ravi and Meera in the same locality, so the newest are not
        // necessarily on the first twenty.
        var directory = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>($"/v1/members?take=100&localityId={UdaipurLocalityId}");

        var names = directory.GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("fullName").GetString())
            .ToList();

        names.Should().Contain(Named("Ravi Shah"));
        names.Should().Contain(Named("Meera Shah"));
    }

    [Fact]
    public async Task A_different_localitys_members_are_not_returned()
    {
        var directory = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>("/v1/members?localityId=" + Guid.NewGuid());

        directory.GetProperty("items").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task One_member_can_be_read_by_id()
    {
        // API-CONTRACTS.md promised this route from the start and nothing
        // implemented it; the portal's directory screen is what noticed. A
        // directory whose rows cannot be opened is a list.
        var meera = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>($"/v1/members/{_meera}");

        meera.GetProperty("fullName").GetString().Should().Be(Named("Meera Shah"));
    }

    [Fact]
    public async Task Reading_one_by_id_applies_the_same_privacy_rules_as_the_directory()
    {
        // The detail view is exactly where a second copy of the per-field
        // rules would drift into showing more than the list does, so it goes
        // through the same mapper.
        var directory = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>("/v1/members");

        var fromList = directory.GetProperty("items").EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == _meera);

        var direct = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>($"/v1/members/{_meera}");

        foreach (var field in new[] { "mobile", "email", "professionId" })
        {
            direct.GetProperty(field).ToString().Should().Be(
                fromList.GetProperty(field).ToString(),
                $"the detail view must not reveal more {field} than the directory");
        }

        direct.GetProperty("permanentAddress").GetProperty("line").ToString().Should().Be(
            fromList.GetProperty("permanentAddress").GetProperty("line").ToString(),
            "the detail view must not reveal more of the address than the directory");
    }

    [Fact]
    public async Task A_member_of_another_Samaaj_is_not_found_rather_than_forbidden()
    {
        var response = await MemberClient(_ravi, TenantA)
            .GetAsync($"/v1/members/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Area Admin: an administrator of one locality ------------------------
    //
    // The locality_id claim bounds the administrator's view - private fields
    // and unlisted members - and the power to correct. It never shrinks the
    // directory they have as a member: outside their locality they see what
    // any member sees.

    [Fact]
    public async Task An_Area_Admin_sees_their_own_locality_as_an_administrator()
    {
        var directory = await AreaAdminClient(TenantA, UdaipurLocalityId)
            .GetFromJsonAsync<JsonElement>("/v1/members?localityId=" + UdaipurLocalityId);

        var meera = directory.GetProperty("items").EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == _meera);

        meera.GetProperty("email").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Elsewhere_an_Area_Admin_sees_only_what_any_member_sees()
    {
        // Asking for another locality is allowed - it is the member directory -
        // but the private fields stay private.
        // Narrowed to this test's own member by name: the class shares one
        // database, every test seeds a Pune member, and a page holds twenty.
        var directory = await AreaAdminClient(TenantA, UdaipurLocalityId)
            .GetFromJsonAsync<JsonElement>(
                "/v1/members?localityId=" + PuneLocalityId + "&term=" + Uri.EscapeDataString(Named("Pune Member")));

        var pune = directory.GetProperty("items").EnumerateArray()
            .Single(m => m.GetProperty("id").GetGuid() == _puneMember);

        pune.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_Area_Admin_opening_a_member_elsewhere_gets_the_member_view()
    {
        var pune = await AreaAdminClient(TenantA, UdaipurLocalityId)
            .GetFromJsonAsync<JsonElement>($"/v1/members/{_puneMember}");

        pune.GetProperty("fullName").GetString().Should().Be(Named("Pune Member"));
        pune.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);

        var meera = await AreaAdminClient(TenantA, UdaipurLocalityId)
            .GetFromJsonAsync<JsonElement>($"/v1/members/{_meera}");

        meera.GetProperty("email").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task An_Area_Admin_cannot_correct_a_member_outside_their_locality()
    {
        var response = await AreaAdminClient(TenantA, UdaipurLocalityId).PatchAsJsonAsync(
            $"/v1/members/{_puneMember}/details", new
            {
                fullName = "Renamed",
                dateOfBirth = (DateOnly?)null,
                gender = (string?)null,
                mobile = (string?)null,
                email = (string?)null,
                gautraId = (Guid?)null,
                professionId = (Guid?)null,
                qualificationId = (Guid?)null,
                permanentAddress = BlankAddress(),
                temporaryAddress = (object?)null,
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Area_Admin_corrects_a_member_inside_their_locality()
    {
        var response = await AreaAdminClient(TenantA, UdaipurLocalityId).PatchAsJsonAsync(
            $"/v1/members/{_meera}/details", new
            {
                fullName = "Meera Corrected",
                dateOfBirth = (DateOnly?)null,
                gender = (string?)null,
                mobile = (string?)null,
                email = (string?)null,
                gautraId = (Guid?)null,
                professionId = (Guid?)null,
                qualificationId = (Guid?)null,
                permanentAddress = BlankAddress(),
                temporaryAddress = (object?)null,
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_member_reads_their_own_profile_in_full()
    {
        var me = await MemberClient(_ravi, TenantA).GetFromJsonAsync<JsonElement>("/v1/members/me");

        me.GetProperty("email").GetString().Should().NotBeNullOrEmpty();
        me.GetProperty("privacy").GetProperty("email").GetString().Should().Be("Private");
    }

    [Fact]
    public async Task A_member_updates_their_own_profile()
    {
        var response = await MemberClient(_ravi, TenantA).PatchAsJsonAsync(
            "/v1/members/" + _ravi,
            new
            {
                fullName = Named("Ravi K Shah"),
                permanentAddress = BlankAddress(),
                privacy = PrivacyAll("Private"),
                isListedInDirectory = true,
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("fullName").GetString().Should().Be(Named("Ravi K Shah"));
        body.GetProperty("privacy").GetProperty("mobile").GetString().Should().Be("Private");
    }

    [Fact]
    public async Task A_member_sets_their_marital_status_and_only_its_matrimony_eligibility_is_announced()
    {
        object Body(string? maritalStatus) => new
        {
            fullName = Named("Ravi Shah"),
            permanentAddress = BlankAddress(),
            privacy = PrivacyAll("Private"),
            isListedInDirectory = true,
            maritalStatus,
        };

        var response = await MemberClient(_ravi, TenantA).PatchAsJsonAsync("/v1/members/" + _ravi, Body("Divorced"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("maritalStatus").GetString()
            .Should().Be("Divorced");

        // Saving the same status again says nothing new.
        (await MemberClient(_ravi, TenantA).PatchAsJsonAsync("/v1/members/" + _ravi, Body("Divorced")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();
            // The payload column is jsonb, so the match on its text happens here.
            var announced = (await db.OutboxMessages
                    .Where(m => m.Topic == "members.marital-status.changed.v1")
                    .Select(m => m.Payload)
                    .ToListAsync())
                .Where(payload => payload.Contains(_ravi.ToString()))
                .ToList();

            announced.Should().ContainSingle();
            JsonDocument.Parse(announced[0]).RootElement.GetProperty("eligibleForMatrimony").GetBoolean().Should().BeTrue();
            announced[0].Should().NotContain("Divorced",
                "the payload is kept forever; whether they may keep a biodata is all it needs to say");
        }

        // Theirs alone: another member's view of them does not carry it.
        var seen = await MemberClient(_meera, TenantA).GetFromJsonAsync<JsonElement>("/v1/members/" + _ravi);
        seen.TryGetProperty("maritalStatus", out _).Should().BeFalse();

        (await MemberClient(_ravi, TenantA).PatchAsJsonAsync("/v1/members/" + _ravi, Body("Widowed")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_cannot_edit_someone_else()
    {
        var response = await MemberClient(_ravi, TenantA).PatchAsJsonAsync(
            "/v1/members/" + _meera,
            new
            {
                fullName = "Hijacked",
                permanentAddress = BlankAddress(),
                privacy = PrivacyAll("Public"),
                isListedInDirectory = true,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_of_another_Samaaj_cannot_be_edited_even_by_an_admin_here()
    {
        // The IDOR guard: the write path re-checks the target's tenant rather
        // than trusting the query filter alone.
        var response = await AdminClient(TenantA).PatchAsJsonAsync(
            "/v1/members/" + _outsider,
            new
            {
                fullName = "Hijacked",
                permanentAddress = BlankAddress(),
                privacy = PrivacyAll("Public"),
                isListedInDirectory = true,
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_invalid_privacy_level_is_a_validation_problem()
    {
        var response = await MemberClient(_ravi, TenantA).PatchAsJsonAsync(
            "/v1/members/" + _ravi,
            new
            {
                fullName = "Ravi Shah",
                permanentAddress = BlankAddress(),
                privacy = new
                {
                    mobile = "Whenever",
                    email = "Private",
                    address = "Private",
                    profession = "Private",
                    dateOfBirth = "Private",
                    qualification = "Private",
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Omitting_the_privacy_object_is_refused_rather_than_crashing()
    {
        // This answered 500 for every caller, including a member editing their
        // own profile. `Privacy` is a non-nullable reference type on the
        // command, which is a compile-time claim and nothing more - the JSON
        // deserialiser leaves it null when the body omits it, and the
        // validator's five sub-rules dereferenced it anyway, because a NotNull
        // rule above them does not stop the ones after it.
        //
        // Found by scripts/tenant-isolation-probe.sh, which was probing for
        // something else entirely.
        var response = await MemberClient(_ravi, TenantA).PatchAsJsonAsync(
            "/v1/members/" + _ravi,
            new { fullName = Named("Ravi No Privacy"), permanentAddress = BlankAddress() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadAsStringAsync();

        problem.Should().Contain("Privacy levels are required");
    }

    [Fact]
    public async Task Omitting_privacy_on_somebody_else_profile_is_still_not_a_500()
    {
        // The same body aimed at another member. Whatever the answer is, it is
        // a decision rather than a crash - a 500 here would be the service
        // telling an attacker it had reached code it did not expect to.
        var response = await MemberClient(_ravi, TenantA).PatchAsJsonAsync(
            "/v1/members/" + _meera,
            new { fullName = Named("Not Mine"), permanentAddress = BlankAddress() });

        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    // ---- Correcting somebody else's details ---------------------------------
    //
    // `Members.Write` was granted, `SERVICES.md` said an administrator could
    // correct anyone's profile, and the only command that let them do it
    // required privacy levels no administrator could read. These cover the
    // separate path, and - more importantly - that the old one is now closed.

    private async Task<MemberProfile> ReloadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        return await db.MemberProfiles.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
    }

    private static object Correction(string fullName, string? mobile = "+919812345678") => new
    {
        fullName,
        dateOfBirth = "1990-01-01",
        gender = "Unspecified",
        mobile,
        email = "kept@example.com",
        gautraId = (Guid?)null,
        professionId = (Guid?)null,
        qualificationId = (Guid?)null,
        permanentAddress = new { line = "An address", cityId = (Guid?)null, localityId = UdaipurLocalityId },
        temporaryAddress = (object?)null,
    };

    [Fact]
    public async Task An_administrator_corrects_another_members_details()
    {
        var response = await AdminClient(TenantA).PatchAsJsonAsync(
            "/v1/members/" + _meera + "/details",
            Correction(Named("Meera Shaha")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReloadAsync(_meera)).FullName.Should().Be(Named("Meera Shaha"));
    }

    [Fact]
    public async Task And_it_cannot_touch_what_that_member_chose_to_share()
    {
        var before = await ReloadAsync(_meera);
        var privacyBefore = before.Privacy;

        (await AdminClient(TenantA).PatchAsJsonAsync(
            "/v1/members/" + _meera + "/details",
            Correction(Named("Meera Corrected"), mobile: "+919800000000")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await ReloadAsync(_meera);

        after.Mobile.Should().Be("+919800000000");

        // The whole reason this endpoint exists. The request carries no privacy
        // fields, so there is nothing an administrator could have sent by
        // accident and nothing they had to guess.
        after.Privacy.Should().Be(privacyBefore);
        after.IsListedInDirectory.Should().BeTrue();
    }

    [Fact]
    public async Task A_member_without_Members_Write_cannot_correct_anybody()
    {
        (await MemberClient(_ravi, TenantA).PatchAsJsonAsync(
            "/v1/members/" + _meera + "/details",
            Correction(Named("Hijacked"))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_administrator_of_another_Samaaj_is_told_no_such_member()
    {
        // The IDOR guard again, on the new write path. A 403 here would confirm
        // the member exists somewhere.
        (await AdminClient(TenantA).PatchAsJsonAsync(
            "/v1/members/" + _outsider + "/details",
            Correction(Named("Hijacked"))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_administrator_correcting_themselves_is_sent_to_their_own_screen()
    {
        // Not a refusal of the change, a refusal of the route. Their own screen
        // can set privacy; this one cannot, and silently dropping that for the
        // one caller entitled to it would be worse than saying so.
        var admin = Guid.NewGuid();

        await SeedAsync(admin, TenantA, Named("An Admin"), UdaipurLocalityId);

        var client = factory.CreateClientAs(
            admin, TenantA, ["SamaajAdmin"], ["Members.Read", "Members.Write"]);

        var response = await client.PatchAsJsonAsync(
            "/v1/members/" + admin + "/details", Correction(Named("An Admin")));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await response.Content.ReadAsStringAsync())
            .Should().Contain("your profile screen");
    }

    [Fact]
    public async Task The_whole_profile_update_is_now_the_members_own_and_nobody_elses()
    {
        // The half of this change that removes something. An administrator
        // sending a complete profile - the shape that carries privacy - is
        // refused however much permission they hold, because there is no body
        // they could send that does not decide something for the member.
        var response = await AdminClient(TenantA).PatchAsJsonAsync(
            "/v1/members/" + _meera,
            new
            {
                fullName = Named("Meera Shah"),
                permanentAddress = BlankAddress(),
                privacy = PrivacyAll("Public"),
                isListedInDirectory = true,
            });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await response.Content.ReadAsStringAsync())
            .Should().Contain("/details");

        // And it changed nothing on the way to being refused.
        (await ReloadAsync(_meera)).Privacy.Email.Should().Be(PrivacyLevel.Private);
    }
}
