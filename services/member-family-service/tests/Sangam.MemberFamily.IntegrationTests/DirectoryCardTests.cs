using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// The directory as cards: a father and mother from the member's household,
/// Gautra and Profession filters, and a page that really is the next page.
/// </summary>
public sealed class DirectoryCardTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    // A tenant of its own, so nothing another test class seeds is counted here.
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Jain = Guid.NewGuid();
    private static readonly Guid Doctor = Guid.NewGuid();

    private static readonly Guid Mahesh = Guid.NewGuid();
    private static readonly Guid Sunita = Guid.NewGuid();
    private static readonly Guid Rahul = Guid.NewGuid();
    private static readonly Guid Kavita = Guid.NewGuid();

    private static bool _seeded;
    private static readonly SemaphoreSlim Seeding = new(1, 1);

    public async Task InitializeAsync()
    {
        await Seeding.WaitAsync();

        try
        {
            if (!_seeded)
            {
                await SeedAsync();
                _seeded = true;
            }
        }
        finally
        {
            Seeding.Release();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Mahesh (the admin) and Sunita (his spouse, unlisted) with their son
    /// Rahul. Mahesh and Rahul are both doctors - Mahesh shares it, Rahul keeps
    /// it Private. Kavita lives alone and has no Gautra.
    /// </summary>
    private async Task SeedAsync()
    {
        await SeedProfileAsync(Mahesh, "Mahesh Jain", Gender.Male, Jain, Doctor, PrivacyLevel.SamaajOnly, listed: true);
        await SeedProfileAsync(Sunita, "Sunita Jain", Gender.Female, Jain, null, PrivacyLevel.SamaajOnly, listed: false);
        await SeedProfileAsync(Rahul, "Rahul Jain", Gender.Male, Jain, Doctor, PrivacyLevel.Private, listed: true);
        await SeedProfileAsync(Kavita, "Kavita Mehta", Gender.Female, null, null, PrivacyLevel.SamaajOnly, listed: true);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();
        var now = DateTimeOffset.UtcNow;

        var family = Family.Create(Tenant, Mahesh, "JN" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), now);

        foreach (var (who, relationship) in new[] { (Sunita, Relationship.Spouse), (Rahul, Relationship.Child) })
        {
            var request = family.RequestJoin(who, relationship, now)!;
            family.DecideJoinRequest(request.Id, accepted: true, Mahesh, now);
        }

        db.Families.Add(family);
        await db.SaveChangesAsync();
    }

    private async Task SeedProfileAsync(
        Guid id, string name, Gender gender, Guid? gautraId, Guid? professionId, PrivacyLevel professionPrivacy, bool listed)
    {
        // One scope per profile: EF tracks an owned value on one owner at a time.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var profile = MemberProfile.FromRegistration(id, Tenant, name, $"{id:N}@example.com", DateTimeOffset.UtcNow);

        profile.Update(
            name,
            dateOfBirth: null,
            gender: gender,
            mobile: null,
            email: $"{id:N}@example.com",
            gautraId: gautraId,
            professionId: professionId,
            qualificationId: null,
            permanentAddress: ProfileAddress.Empty,
            temporaryAddress: null,
            privacy: new FieldPrivacy(
                PrivacyLevel.SamaajOnly,
                PrivacyLevel.Private,
                PrivacyLevel.Private,
                professionPrivacy,
                PrivacyLevel.SamaajOnly,
                PrivacyLevel.SamaajOnly),
            isListedInDirectory: listed,
            updatedAt: DateTimeOffset.UtcNow,
            updatedBy: id);

        db.MemberProfiles.Add(profile);
        await db.SaveChangesAsync();
    }

    private HttpClient Member(Guid? userId = null) =>
        factory.CreateClientAs(userId ?? Guid.NewGuid(), Tenant, ["Member"], ["Members.Read"]);

    private HttpClient Admin() =>
        factory.CreateClientAs(Guid.NewGuid(), Tenant, ["Member", "SamaajAdmin"], ["Members.Read", "Members.Write"]);

    private static async Task<List<JsonElement>> CardsAsync(HttpClient client, string query = "")
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/v1/members" + query);

        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private static List<string?> Names(IEnumerable<JsonElement> cards) =>
        [.. cards.Select(card => card.GetProperty("fullName").GetString())];

    private static JsonElement Card(IEnumerable<JsonElement> cards, Guid id) =>
        cards.Single(card => card.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task A_child_card_names_the_admin_as_father_and_an_admin_sees_the_unlisted_mother()
    {
        var rahul = Card(await CardsAsync(Admin()), Rahul);

        rahul.GetProperty("fatherName").GetString().Should().Be("Mahesh Jain");
        rahul.GetProperty("motherName").GetString().Should().Be("Sunita Jain");
    }

    [Fact]
    public async Task A_member_is_not_shown_a_parent_who_took_themselves_out_of_the_directory()
    {
        var rahul = Card(await CardsAsync(Member()), Rahul);

        rahul.GetProperty("fatherName").GetString().Should().Be("Mahesh Jain");
        rahul.GetProperty("motherName").ValueKind.Should().Be(JsonValueKind.Null,
            "Sunita is unlisted, and a relative's card should not bring her back");
    }

    [Fact]
    public async Task Parents_the_household_does_not_record_are_left_blank()
    {
        var cards = await CardsAsync(Admin());

        // Mahesh is the admin with no Parent row; Kavita has no household.
        foreach (var id in new[] { Mahesh, Kavita })
        {
            Card(cards, id).GetProperty("fatherName").ValueKind.Should().Be(JsonValueKind.Null);
            Card(cards, id).GetProperty("motherName").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task Gautra_filters_plainly()
    {
        Names(await CardsAsync(Member(), $"?gautraId={Jain}"))
            .Should().Equal("Mahesh Jain", "Rahul Jain");
    }

    [Fact]
    public async Task Profession_matches_only_what_the_viewer_could_already_read()
    {
        // Rahul keeps his profession Private. If a member's filter matched him,
        // asking for "Doctor" would confirm what he chose not to share.
        Names(await CardsAsync(Member(), $"?professionId={Doctor}"))
            .Should().Equal("Mahesh Jain");

        Names(await CardsAsync(Admin(), $"?professionId={Doctor}"))
            .Should().Equal("Mahesh Jain", "Rahul Jain");

        Names(await CardsAsync(Member(Rahul), $"?professionId={Doctor}"))
            .Should().Equal(["Mahesh Jain", "Rahul Jain"], "his own profession is no secret from him");
    }

    [Fact]
    public async Task The_second_page_is_the_second_page()
    {
        // Skip was accepted and never applied, so "Next" showed page one again.
        var first = Names(await CardsAsync(Member(), "?skip=0&take=1"));
        var second = Names(await CardsAsync(Member(), "?skip=1&take=1"));

        first.Should().Equal("Kavita Mehta");
        second.Should().Equal("Mahesh Jain");
    }
}
