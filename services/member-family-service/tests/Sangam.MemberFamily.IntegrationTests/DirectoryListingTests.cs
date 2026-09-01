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
/// Taking yourself out of the member directory, and the line between being
/// unlisted and being unreachable.
/// </summary>
public sealed class DirectoryListingTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly Guid _listed = Guid.NewGuid();
    private readonly Guid _unlisted = Guid.NewGuid();

    /// <summary>
    /// The class fixture shares one database across the class, so seeded names
    /// carry a suffix that makes this run's members distinguishable.
    /// </summary>
    private readonly string _run = Guid.NewGuid().ToString("N")[..6];

    public async Task InitializeAsync()
    {
        await SeedAsync(_listed, Named("Ravi Shah"), listed: true);
        await SeedAsync(_unlisted, Named("Meera Shah"), listed: false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private string Named(string name) => name + " " + _run;

    private async Task SeedAsync(Guid id, string name, bool listed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var profile = MemberProfile.FromRegistration(
            id, TenantId, name, id.ToString("N") + "@example.com", DateTimeOffset.UtcNow);

        profile.Update(
            name,
            photoUrl: null,
            dateOfBirth: null,
            gender: Gender.Unspecified,
            mobile: "+919812345678",
            email: id.ToString("N") + "@example.com",
            address: null,
            locality: "Udaipur",
            profession: null,
            FieldPrivacy.Default,
            isListedInDirectory: listed,
            DateTimeOffset.UtcNow,
            Guid.NewGuid());

        db.MemberProfiles.Add(profile);
        await db.SaveChangesAsync();
    }

    private HttpClient MemberClient(Guid userId) =>
        factory.CreateClientAs(userId, TenantId, ["Member"], ["Members.Read"]);

    private HttpClient AdminClient() =>
        factory.CreateClientAs(
            Guid.NewGuid(), TenantId, ["SamaajAdmin"], ["Members.Read", "Members.Write"]);

    private static async Task<List<Guid>> IdsAsync(HttpClient client)
    {
        var directory = await client.GetFromJsonAsync<JsonElement>("/v1/members");

        return [.. directory.EnumerateArray().Select(m => m.GetProperty("id").GetGuid())];
    }

    [Fact]
    public async Task An_unlisted_member_is_not_in_the_directory()
    {
        var ids = await IdsAsync(MemberClient(_listed));

        ids.Should().Contain(_listed);
        ids.Should().NotContain(_unlisted);
    }

    [Fact]
    public async Task Searching_for_them_by_name_finds_nothing_either()
    {
        // Otherwise the setting would only hide them from a list nobody scrolls,
        // which is not what the checkbox says.
        var directory = await MemberClient(_listed)
            .GetFromJsonAsync<JsonElement>($"/v1/members?term={Uri.EscapeDataString(Named("Meera Shah"))}");

        directory.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task An_unlisted_member_can_still_see_the_directory()
    {
        // Being unlisted is about how others find you, not about what you can
        // do. A member who took themselves out and then could not use the
        // directory would have been punished for a privacy choice.
        var ids = await IdsAsync(MemberClient(_unlisted));

        ids.Should().Contain(_listed);
    }

    [Fact]
    public async Task An_unlisted_member_is_still_reachable_by_id()
    {
        // The distinction the whole design rests on. A volunteer group's
        // president has to see who applied; a timeline post has an author. If
        // being unlisted made a profile 404 those would break, and the setting
        // would be an access control it was never meant to be.
        var response = await MemberClient(_listed).GetAsync($"/v1/members/{_unlisted}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_administrator_still_finds_them()
    {
        // Correcting a member's details is administrative work, and a member an
        // administrator cannot look up is a member nobody can help.
        var ids = await IdsAsync(AdminClient());

        ids.Should().Contain(_unlisted);
    }

    [Fact]
    public async Task A_member_takes_themselves_out_through_the_endpoint()
    {
        var response = await MemberClient(_listed).PatchAsJsonAsync(
            $"/v1/members/{_listed}",
            new
            {
                fullName = Named("Ravi Shah"),
                gender = "Male",
                mobile = "+919812345678",
                privacy = new
                {
                    mobile = "SamaajOnly",
                    email = "Private",
                    address = "Private",
                    profession = "SamaajOnly",
                    dateOfBirth = "Private",
                },
                isListedInDirectory = false,
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("isListedInDirectory").GetBoolean().Should().BeFalse();

        (await IdsAsync(MemberClient(_unlisted))).Should().NotContain(_listed);
    }

    [Fact]
    public async Task An_update_that_omits_the_setting_is_refused_rather_than_re_listing_them()
    {
        // The failure this validation exists to prevent: a client that does not
        // know about the field would put a member who had opted out back into
        // the directory, silently, because they edited their address.
        var response = await MemberClient(_unlisted).PatchAsJsonAsync(
            $"/v1/members/{_unlisted}",
            new
            {
                fullName = Named("Meera Shah"),
                gender = "Female",
                privacy = new
                {
                    mobile = "SamaajOnly",
                    email = "Private",
                    address = "Private",
                    profession = "SamaajOnly",
                    dateOfBirth = "Private",
                },
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stillHidden = await factory.Services.CreateAsyncScope().ServiceProvider
            .GetRequiredService<MemberFamilyDbContext>()
            .MemberProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == _unlisted)
            .Select(p => p.IsListedInDirectory)
            .SingleAsync();

        stillHidden.Should().BeFalse();
    }
}
