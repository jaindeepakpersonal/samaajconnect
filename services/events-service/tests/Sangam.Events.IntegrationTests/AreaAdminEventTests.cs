using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.Events.Application.Security;
using Xunit;

namespace Sangam.Events.IntegrationTests;

/// <summary>
/// An Area Admin administers the events of one locality - and remains a member
/// of the whole Samaaj, who can see and register for any published event.
/// </summary>
public sealed class AreaAdminEventTests(EventsApiFactory factory)
    : IClassFixture<EventsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Hiran = Guid.NewGuid();
    private static readonly Guid Sector14 = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient SamaajAdmin() =>
        factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.SamaajAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.EventsPublish]);

    private HttpClient AreaAdmin(Guid locality) =>
        factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.Member, Roles.AreaAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.EventsPublish], locality);

    private static object NewEvent(string title, Guid? locality) => new
    {
        title,
        description = (string?)null,
        startAt = DateTimeOffset.UtcNow.AddDays(7),
        endAt = (DateTimeOffset?)null,
        venue = (string?)null,
        eventTypeId = (string?)null,
        organizerType = "Samaaj",
        organizerId = (Guid?)null,
        registrationEnabled = true,
        capacity = (int?)null,
        localityId = locality?.ToString(),
    };

    private async Task<Guid> DraftAsync(string title, Guid? locality)
    {
        var response = await SamaajAdmin().PostAsJsonAsync("/v1/events", NewEvent(title, locality));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task An_event_an_Area_Admin_sets_up_lands_in_their_own_locality()
    {
        var response = await AreaAdmin(Hiran).PostAsJsonAsync("/v1/events", NewEvent("Pravachan", null));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("localityId").GetGuid().Should().Be(Hiran);
    }

    [Fact]
    public async Task An_Area_Admin_cannot_set_up_an_event_in_another_locality()
    {
        (await AreaAdmin(Hiran).PostAsJsonAsync("/v1/events", NewEvent("Pravachan", Sector14)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Area_Admin_publishes_their_own_locality_s_events_and_no_others()
    {
        var own = await DraftAsync("Hiran Magri Pravachan", Hiran);
        var elsewhere = await DraftAsync("Sector 14 Pravachan", Sector14);
        var samaajWide = await DraftAsync("Paryushan", null);

        (await AreaAdmin(Hiran).PostAsync($"/v1/events/{own}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AreaAdmin(Hiran).PostAsync($"/v1/events/{elsewhere}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await AreaAdmin(Hiran).PostAsync($"/v1/events/{samaajWide}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Area_Admin_sees_only_their_own_locality_s_drafts_but_every_published_event()
    {
        var ownDraft = await DraftAsync("Hiran Magri Pravachan", Hiran);
        var otherDraft = await DraftAsync("Sector 14 Pravachan", Sector14);
        var otherPublished = await DraftAsync("Sector 14 Mela", Sector14);

        (await SamaajAdmin().PostAsync($"/v1/events/{otherPublished}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var listed = (await AreaAdmin(Hiran).GetFromJsonAsync<JsonElement>("/v1/events?includeDrafts=true"))
            .EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

        listed.Should().Contain(ownDraft);
        listed.Should().Contain(otherPublished);
        listed.Should().NotContain(otherDraft);

        (await AreaAdmin(Hiran).GetAsync($"/v1/events/{otherDraft}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_Area_Admin_reads_attendees_only_for_their_own_locality()
    {
        var elsewhere = await DraftAsync("Sector 14 Mela", Sector14);

        (await AreaAdmin(Hiran).GetAsync($"/v1/events/{elsewhere}/attendees"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task As_a_member_an_Area_Admin_registers_for_an_event_elsewhere()
    {
        var elsewhere = await DraftAsync("Sector 14 Mela", Sector14);
        await SamaajAdmin().PostAsync($"/v1/events/{elsewhere}/publish", null);

        (await AreaAdmin(Hiran).PostAsync($"/v1/events/{elsewhere}/registration", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
