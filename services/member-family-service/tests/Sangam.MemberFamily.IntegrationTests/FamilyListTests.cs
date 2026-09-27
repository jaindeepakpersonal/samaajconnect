using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// The admin panel's household list: filtered by where the members live,
/// administrators only, and an Area Admin only for their own locality.
/// </summary>
public sealed class FamilyListTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    // A tenant of its own, so nothing another test class seeds is counted here.
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OtherTenant = Guid.NewGuid();
    private static readonly Guid Udaipur = Guid.NewGuid();
    private static readonly Guid Pune = Guid.NewGuid();

    private static readonly Guid UdaipurHead = Guid.NewGuid();
    private static readonly Guid PuneHead = Guid.NewGuid();
    private static Guid _udaipurFamily;
    private static Guid _puneFamily;

    private static readonly SemaphoreSlim Seeding = new(1, 1);

    public async Task InitializeAsync()
    {
        await Seeding.WaitAsync();

        try
        {
            if (_udaipurFamily == Guid.Empty)
            {
                await SeedAsync();
            }
        }
        finally
        {
            Seeding.Release();
        }
    }

    private async Task SeedAsync()
    {
        var now = DateTimeOffset.UtcNow;

        // One scope per profile: a new profile's default privacy is one shared
        // value object, and EF tracks an owned instance on one owner at a time.
        foreach (var (id, name) in new[] { (UdaipurHead, "Ravi Shah"), (PuneHead, "Anil Mehta") })
        {
            await using var profileScope = factory.Services.CreateAsyncScope();
            var profiles = profileScope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

            profiles.MemberProfiles.Add(MemberProfile.FromRegistration(id, Tenant, name, $"{id:N}@example.com", now));
            await profiles.SaveChangesAsync();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var udaipur = Family.Create(Tenant, UdaipurHead, "UDR" + Guid.NewGuid().ToString("N")[..6], now.AddMinutes(-2));
        var pune = Family.Create(Tenant, PuneHead, "PNE" + Guid.NewGuid().ToString("N")[..6], now.AddMinutes(-1));

        db.Families.AddRange(udaipur, pune);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.ChildProfiles.Add(ChildProfile.Create(
            Tenant, udaipur.Id, "Diya Shah", today.AddYears(-10).AddDays(-1), Gender.Female, UdaipurHead, now));

        await db.SaveChangesAsync();

        // The address, as SQL: this test is about the list, not about editing a profile.
        await db.Database.ExecuteSqlAsync(
            $"UPDATE member_profiles SET permanent_address_locality_id = {Udaipur} WHERE id = {UdaipurHead}");
        await db.Database.ExecuteSqlAsync(
            $"UPDATE member_profiles SET permanent_address_locality_id = {Pune} WHERE id = {PuneHead}");

        _udaipurFamily = udaipur.Id;
        _puneFamily = pune.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin(Guid? tenant = null) =>
        factory.CreateClientAs(Guid.NewGuid(), tenant ?? Tenant, ["Member", "SamaajAdmin"], ["Members.Read", "Members.Write"]);

    private HttpClient AreaAdmin() =>
        factory.CreateClientAs(Guid.NewGuid(), Tenant, ["Member", "AreaAdmin"], ["Members.Read", "Members.Write"], Udaipur);

    private static List<Guid> Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(f => f.GetProperty("id").GetGuid())];

    [Fact]
    public async Task An_administrator_sees_every_household_with_its_members_and_children()
    {
        var page = await Admin().GetFromJsonAsync<JsonElement>("/v1/families");

        Ids(page).Should().Equal([_puneFamily, _udaipurFamily], "newest first");

        var udaipur = page.GetProperty("items").EnumerateArray().Single(f => f.GetProperty("id").GetGuid() == _udaipurFamily);
        var head = udaipur.GetProperty("members").EnumerateArray().Single();
        head.GetProperty("fullName").GetString().Should().Be("Ravi Shah");
        head.GetProperty("localityId").GetGuid().Should().Be(Udaipur);

        var child = udaipur.GetProperty("children").EnumerateArray().Single();
        child.GetProperty("fullName").GetString().Should().Be("Diya Shah");
        child.GetProperty("age").GetInt32().Should().Be(10);
        child.TryGetProperty("dateOfBirth", out _).Should().BeFalse("an age, never a birthday");
    }

    [Fact]
    public async Task A_locality_narrows_to_households_with_a_member_living_there()
    {
        Ids(await Admin().GetFromJsonAsync<JsonElement>($"/v1/families?localityId={Udaipur}"))
            .Should().Equal(_udaipurFamily);

        Ids(await Admin().GetFromJsonAsync<JsonElement>($"/v1/families?localityId={Guid.NewGuid()}"))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task An_Area_Admin_lists_their_own_locality_and_nothing_wider()
    {
        Ids(await AreaAdmin().GetFromJsonAsync<JsonElement>($"/v1/families?localityId={Udaipur}"))
            .Should().Equal(_udaipurFamily);

        (await AreaAdmin().GetAsync($"/v1/families?localityId={Pune}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AreaAdmin().GetAsync("/v1/families")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_member_cannot_list_households_and_another_Samaaj_sees_none_of_these()
    {
        (await factory.CreateClientAs(Guid.NewGuid(), Tenant, ["Member"], ["Members.Read"]).GetAsync("/v1/families"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        Ids(await Admin(OtherTenant).GetFromJsonAsync<JsonElement>("/v1/families"))
            .Should().NotContain([_udaipurFamily, _puneFamily]);
    }
}
