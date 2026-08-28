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

    private readonly Guid _ravi = Guid.NewGuid();
    private readonly Guid _meera = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();

    /// <summary>
    /// Suffix making this test method's seeded members distinguishable. The
    /// class fixture shares one database across the class, so without it every
    /// test would find every other test's Meera Shah.
    /// </summary>
    private readonly string _run = Guid.NewGuid().ToString("N")[..6];

    public async Task InitializeAsync()
    {
        await SeedAsync(_ravi, TenantA, Named("Ravi Shah"), "Udaipur");
        await SeedAsync(_meera, TenantA, Named("Meera Shah"), "Udaipur");
        await SeedAsync(_outsider, TenantB, Named("Someone Else"), "Pune");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync(Guid id, Guid tenantId, string name, string locality)
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
            photoUrl: null,
            dateOfBirth: new DateOnly(1990, 1, 1),
            gender: Gender.Unspecified,
            mobile: "+919812345678",
            email: contact,
            address: "An address",
            locality: locality,
            profession: "Architect",
            new FieldPrivacy(
                PrivacyLevel.SamaajOnly,
                PrivacyLevel.Private,
                PrivacyLevel.Private,
                PrivacyLevel.SamaajOnly,
                PrivacyLevel.Private),
            DateTimeOffset.UtcNow, Guid.NewGuid());

        db.MemberProfiles.Add(profile);
        await db.SaveChangesAsync();
    }

    private HttpClient MemberClient(Guid userId, Guid tenantId) =>
        factory.CreateClientAs(userId, tenantId, ["Member"], ["Members.Read"]);

    private HttpClient AdminClient(Guid tenantId) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId, ["SamaajAdmin"], ["Members.Read", "Members.Write"]);

    private string Named(string name) => name + " " + _run;

    private static object PrivacyAll(string level) => new
    {
        mobile = level,
        email = level,
        address = level,
        profession = level,
        dateOfBirth = level,
    };

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

        var names = directory.EnumerateArray()
            .Select(m => m.GetProperty("fullName").GetString())
            .ToList();

        names.Should().Contain(Named("Meera Shah"));
        names.Should().NotContain(Named("Someone Else"));
    }

    [Fact]
    public async Task Another_members_private_fields_come_back_null()
    {
        var directory = await MemberClient(_ravi, TenantA).GetFromJsonAsync<JsonElement>("/v1/members");

        var meera = directory.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == _meera);

        meera.GetProperty("mobile").GetString().Should().NotBeNullOrEmpty();
        meera.GetProperty("email").ValueKind.Should().Be(JsonValueKind.Null);
        meera.GetProperty("address").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_Samaaj_admin_sees_the_private_fields_they_may_need_to_correct()
    {
        var directory = await AdminClient(TenantA).GetFromJsonAsync<JsonElement>("/v1/members");

        var meera = directory.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == _meera);

        meera.GetProperty("email").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Searching_matches_on_name()
    {
        var directory = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>("/v1/members?term=" + Uri.EscapeDataString(Named("Meera Shah")));

        directory.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("fullName").GetString().Should().Be(Named("Meera Shah"));
    }

    [Fact]
    public async Task Searching_does_not_match_on_a_private_contact_detail()
    {
        // Otherwise a private number could be confirmed one guess at a time,
        // whatever the field's privacy level said afterwards.
        var directory = await MemberClient(_ravi, TenantA)
            .GetFromJsonAsync<JsonElement>("/v1/members?term=919812345678");

        directory.EnumerateArray().Should().BeEmpty();
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
            new { fullName = Named("Ravi K Shah"), locality = "Jaipur", privacy = PrivacyAll("Private") });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("fullName").GetString().Should().Be(Named("Ravi K Shah"));
        body.GetProperty("privacy").GetProperty("mobile").GetString().Should().Be("Private");
    }

    [Fact]
    public async Task A_member_cannot_edit_someone_else()
    {
        var response = await MemberClient(_ravi, TenantA).PatchAsJsonAsync(
            "/v1/members/" + _meera,
            new { fullName = "Hijacked", privacy = PrivacyAll("Public") });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_of_another_Samaaj_cannot_be_edited_even_by_an_admin_here()
    {
        // The IDOR guard: the write path re-checks the target's tenant rather
        // than trusting the query filter alone.
        var response = await AdminClient(TenantA).PatchAsJsonAsync(
            "/v1/members/" + _outsider,
            new { fullName = "Hijacked", privacy = PrivacyAll("Public") });

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
                privacy = new
                {
                    mobile = "Whenever",
                    email = "Private",
                    address = "Private",
                    profession = "Private",
                    dateOfBirth = "Private",
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
