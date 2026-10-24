using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.Events.Application.Security;
using Xunit;

namespace Sangam.Events.IntegrationTests;

/// <summary>
/// An event's schedule of programmes through the endpoints, and an event that
/// is under way staying on the list until it is over.
/// </summary>
public sealed class EventScheduleTests(EventsApiFactory factory)
    : IClassFixture<EventsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid Hiran = Guid.NewGuid();
    private static readonly Guid Sector14 = Guid.NewGuid();

    /// <summary>When the scheduled events start - a month out, so they are upcoming whenever the suite runs.</summary>
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(30);

    /// <summary>A programme date, counted in days from the start.</summary>
    private static string Day(int fromStart) =>
        DateOnly.FromDateTime(Start.UtcDateTime).AddDays(fromStart).ToString("yyyy-MM-dd");

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid? tenantId = null) =>
        factory.CreateClientAs(Guid.NewGuid(), tenantId ?? TenantId, [Roles.Member], [PermissionKeys.MembersRead]);

    private HttpClient Organiser(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantId, [Roles.SamaajAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.EventsPublish]);

    private HttpClient AreaAdmin(Guid locality) =>
        factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.Member, Roles.AreaAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.EventsPublish], locality);

    private async Task<Guid> PublishedAsync(
        DateTimeOffset startAt, DateTimeOffset? endAt, string title = "Paryushan Parva", Guid? localityId = null)
    {
        var response = await Organiser().PostAsJsonAsync("/v1/events", new
        {
            title,
            description = (string?)null,
            startAt,
            endAt,
            venue = "Upashray",
            organizerType = "Samaaj",
            organizerId = (Guid?)null,
            registrationEnabled = false,
            capacity = (int?)null,
            localityId = localityId?.ToString(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await Organiser().PostAsync($"/v1/events/{id}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        return id;
    }

    private static object Programme(string date, string name, string start, string? end) =>
        new { date, programName = name, startTime = start, endTime = end };

    private static List<string?> ProgrammeNames(JsonElement samaajEvent) =>
        [.. samaajEvent.GetProperty("schedule").EnumerateArray().Select(s => s.GetProperty("programName").GetString())];

    // ---- The schedule -------------------------------------------------------

    [Fact]
    public async Task An_organiser_builds_a_schedule_and_every_member_reads_it_in_order()
    {
        var id = await PublishedAsync(Start, Start.AddDays(7));

        foreach (var programme in new[]
        {
            Programme(Day(1), "Pratikraman", "18:30:00", null),
            Programme(Day(0), "Pravachan", "10:00:00", "11:30:00"),
            Programme(Day(0), "Prabhat Pheri", "06:00:00", "07:00:00"),
        })
        {
            (await Organiser().PostAsJsonAsync($"/v1/events/{id}/schedule", programme))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var read = await Member().GetFromJsonAsync<JsonElement>($"/v1/events/{id}");

        ProgrammeNames(read).Should().Equal("Prabhat Pheri", "Pravachan", "Pratikraman");

        var pravachan = read.GetProperty("schedule")[1];
        pravachan.GetProperty("date").GetString().Should().Be(Day(0));
        pravachan.GetProperty("startTime").GetString().Should().Be("10:00:00");
        pravachan.GetProperty("endTime").GetString().Should().Be("11:30:00");
        read.GetProperty("schedule")[2].GetProperty("endTime").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_programme_is_corrected_and_removed()
    {
        var id = await PublishedAsync(Start, null);

        var added = await (await Organiser().PostAsJsonAsync(
                $"/v1/events/{id}/schedule", Programme(Day(0), "Pravachan", "10:00:00", null)))
            .Content.ReadFromJsonAsync<JsonElement>();
        var itemId = added.GetProperty("schedule")[0].GetProperty("id").GetGuid();

        var corrected = await Organiser().PutAsJsonAsync(
            $"/v1/events/{id}/schedule/{itemId}", Programme(Day(0), "Pravachan by Muni Shri", "10:30:00", "12:00:00"));

        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        ProgrammeNames(await corrected.Content.ReadFromJsonAsync<JsonElement>()).Should().Equal("Pravachan by Muni Shri");

        (await Organiser().DeleteAsync($"/v1/events/{id}/schedule/{itemId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        ProgrammeNames(await Member().GetFromJsonAsync<JsonElement>($"/v1/events/{id}")).Should().BeEmpty();

        (await Organiser().DeleteAsync($"/v1/events/{id}/schedule/{itemId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "it is already gone");
    }

    [Fact]
    public async Task A_programme_that_ends_before_it_starts_is_refused()
    {
        var id = await PublishedAsync(Start, null);

        (await Organiser().PostAsJsonAsync(
                $"/v1/events/{id}/schedule", Programme(Day(0), "Pravachan", "11:00:00", "10:00:00")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_cannot_change_the_schedule()
    {
        var id = await PublishedAsync(Start, null);

        (await Member().PostAsJsonAsync($"/v1/events/{id}/schedule", Programme(Day(0), "Pravachan", "10:00:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Area_Admin_schedules_only_their_own_localitys_events_and_another_Samaaj_none()
    {
        var own = await PublishedAsync(Start, null, localityId: Hiran);
        var elsewhere = await PublishedAsync(Start, null, localityId: Sector14);

        (await AreaAdmin(Hiran).PostAsJsonAsync($"/v1/events/{own}/schedule", Programme(Day(0), "Pravachan", "10:00:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AreaAdmin(Hiran).PostAsJsonAsync($"/v1/events/{elsewhere}/schedule", Programme(Day(0), "Pravachan", "10:00:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Organiser(OtherTenantId).PostAsJsonAsync($"/v1/events/{own}/schedule", Programme(Day(0), "Pravachan", "10:00:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_cancelled_events_schedule_cannot_change()
    {
        var id = await PublishedAsync(Start, null);

        (await Organiser().PostAsJsonAsync($"/v1/events/{id}/cancel", new { reason = "Postponed." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await Organiser().PostAsJsonAsync($"/v1/events/{id}/schedule", Programme(Day(0), "Pravachan", "10:00:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_programme_outside_the_events_own_dates_is_refused()
    {
        // A one-day event takes programmes on its own day - with a day's slack
        // for the time zone the service is not told - and on no other.
        var id = await PublishedAsync(Start, null);

        (await Organiser().PostAsJsonAsync($"/v1/events/{id}/schedule", Programme(Day(5), "Pravachan", "10:00:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Organiser().PostAsJsonAsync($"/v1/events/{id}/schedule", Programme(Day(-5), "Pravachan", "10:00:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var added = await (await Organiser().PostAsJsonAsync(
                $"/v1/events/{id}/schedule", Programme(Day(0), "Pravachan", "10:00:00", null)))
            .Content.ReadFromJsonAsync<JsonElement>();
        var itemId = added.GetProperty("schedule")[0].GetProperty("id").GetGuid();

        (await Organiser().PutAsJsonAsync($"/v1/events/{id}/schedule/{itemId}", Programme(Day(9), "Pravachan", "10:00:00", null)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a correction is held to the same dates");
    }

    // ---- What "upcoming" means ---------------------------------------------

    [Fact]
    public async Task An_event_under_way_stays_on_the_list_until_it_is_over()
    {
        var now = DateTimeOffset.UtcNow;

        var underWay = await PublishedAsync(now.AddHours(-2), now.AddHours(3), "Under way");
        var untimed = await PublishedAsync(now.AddHours(-4), null, "Started this morning, no end");
        var over = await PublishedAsync(now.AddDays(-2), now.AddDays(-1), "Finished yesterday");
        var later = await PublishedAsync(now.AddDays(5), now.AddDays(5).AddHours(2), "Next week");

        var list = await Member().GetFromJsonAsync<JsonElement>("/v1/events");
        var shown = list.EnumerateArray().ToDictionary(e => e.GetProperty("id").GetGuid());

        shown.Keys.Should().BeEquivalentTo([underWay, untimed, later]);
        shown.Should().NotContainKey(over);

        shown[underWay].GetProperty("isHappeningNow").GetBoolean().Should().BeTrue();
        shown[untimed].GetProperty("isHappeningNow").GetBoolean().Should().BeTrue();
        shown[later].GetProperty("isHappeningNow").GetBoolean().Should().BeFalse();

        var everything = await Member().GetFromJsonAsync<JsonElement>("/v1/events?includePast=true");
        everything.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Contain(over);
    }
}
