using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// Resolving child ids to names for an administrator, and the three things that
/// lookup must not become.
/// </summary>
public sealed class ChildNamesTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Guid _head = Guid.NewGuid();
    private readonly Guid _outsideHead = Guid.NewGuid();

    private Guid _aarav;
    private Guid _elsewhere;

    public async Task InitializeAsync()
    {
        _aarav = await SeedChildAsync(TenantA, _head, "Aarav Shah");
        _elsewhere = await SeedChildAsync(TenantB, _outsideHead, "Someone Elses Child");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedChildAsync(Guid tenantId, Guid headId, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var family = Family.Create(tenantId, headId, Family.GenerateCode(), DateTimeOffset.UtcNow);
        db.Families.Add(family);

        var child = ChildProfile.Create(
            tenantId,
            family.Id,
            name,
            new DateOnly(2014, 3, 9),
            Gender.Male,
            photoUrl: null,
            headId,
            DateTimeOffset.UtcNow);

        db.ChildProfiles.Add(child);
        await db.SaveChangesAsync();

        return child.Id;
    }

    private HttpClient AdminClient(Guid tenantId) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId, ["SamaajAdmin"], ["Members.Read", "Members.Write"]);

    private HttpClient MemberClient(Guid tenantId) =>
        factory.CreateClientAs(Guid.NewGuid(), tenantId, ["Member"], ["Members.Read"]);

    private static async Task<JsonElement> NamesAsync(HttpClient client, params Guid[] ids) =>
        await client.GetFromJsonAsync<JsonElement>("/v1/children/names?ids=" + string.Join(",", ids));

    [Fact]
    public async Task An_administrator_gets_the_names_they_asked_for()
    {
        var names = await NamesAsync(AdminClient(TenantA), _aarav);

        names.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("fullName").GetString().Should().Be("Aarav Shah");
    }

    [Fact]
    public async Task It_answers_with_names_and_nothing_else()
    {
        // The whole reason this is not ChildResponse. That record carries a date
        // of birth, a gender, a photo and the parental-consent record, and
        // handing those over so a screen can print a name is the opposite of
        // what DPDP section 9 asks of this platform.
        var names = await NamesAsync(AdminClient(TenantA), _aarav);

        var child = names.EnumerateArray().Single();

        child.EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(["id", "fullName"]);
    }

    [Fact]
    public async Task A_child_in_another_Samaaj_is_simply_absent()
    {
        // Not a 403. A caller holding a GUID learns only that this Samaaj has no
        // such child, which is true, rather than that one exists elsewhere.
        var names = await NamesAsync(AdminClient(TenantA), _aarav, _elsewhere);

        var ids = names.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(_aarav);
        ids.Should().NotContain(_elsewhere);
    }

    [Fact]
    public async Task An_ordinary_member_cannot_name_children_by_id()
    {
        // A member sees their own household through /v1/children. This is the
        // administrator's lookup, and a member holding an id is not a reason to
        // answer.
        var response = await MemberClient(TenantA)
            .GetAsync($"/v1/children/names?ids={_aarav}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Asking_for_nothing_is_refused_rather_than_answered_with_everything()
    {
        var response = await AdminClient(TenantA).GetAsync("/v1/children/names?ids=");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Asking_for_too_many_at_once_is_refused()
    {
        // The one query whose cost the caller chooses. Without a cap it is a way
        // to walk the table a request at a time.
        var many = string.Join(",", Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()));

        var response = await AdminClient(TenantA).GetAsync($"/v1/children/names?ids={many}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rubbish_in_the_id_list_is_ignored_rather_than_throwing()
    {
        var response = await AdminClient(TenantA)
            .GetAsync($"/v1/children/names?ids=not-a-guid,{_aarav},,also-not");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var names = await response.Content.ReadFromJsonAsync<JsonElement>();

        names.EnumerateArray().Should().ContainSingle();
    }
}
