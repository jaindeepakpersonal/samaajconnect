using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Events.Application.Security;
using Sangam.Events.Domain.Events;
using Xunit;

namespace Sangam.Events.IntegrationTests;

/// <summary>
/// Events through their endpoints, against a real database.
/// </summary>
/// <remarks>
/// The tenant query filter is applied by the DbContext rather than by any
/// handler; the unique index on (event, member) is what actually holds when two
/// registrations race; and the outbox guarantee is transactional. None of the
/// three can be shown against a substituted repository.
/// </remarks>
public sealed class EventEndpointsTests(EventsApiFactory factory)
    : IClassFixture<EventsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid OrganiserId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? MemberId,
            tenantId ?? TenantId,
            [Roles.Member],
            [PermissionKeys.MembersRead]);

    private HttpClient Organiser(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? OrganiserId,
            tenantId ?? TenantId,
            [Roles.SamaajAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.EventsPublish]);

    private static object NewEvent(int? capacity = null, bool registrationEnabled = true) => new
    {
        title = "Paryushan Lecture",
        description = "An evening lecture on Jain philosophy.",
        startAt = DateTimeOffset.UtcNow.AddDays(30),
        endAt = DateTimeOffset.UtcNow.AddDays(30).AddHours(2),
        venue = "Community Hall",
        organizerType = "Samaaj",
        organizerId = (Guid?)null,
        registrationEnabled,
        capacity,
    };

    private async Task<Guid> CreateAsync(int? capacity = null, bool registrationEnabled = true)
    {
        var response = await Organiser().PostAsJsonAsync(
            "/v1/events", NewEvent(capacity, registrationEnabled));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> PublishedAsync(int? capacity = null, bool registrationEnabled = true)
    {
        var id = await CreateAsync(capacity, registrationEnabled);

        (await Organiser().PostAsync($"/v1/events/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return id;
    }

    private Task<HttpResponseMessage> RegisterAsync(HttpClient client, Guid id) =>
        client.PostAsync($"/v1/events/{id}/registration", null);

    // ---- Creating and publishing -----------------------------------------

    [Fact]
    public async Task An_event_is_created_as_a_draft_and_announces_nothing_yet()
    {
        var id = await CreateAsync();

        var persisted = await factory.WithDbContextAsync(db =>
            db.Events.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.Id == id));

        persisted.Status.Should().Be(EventStatus.Draft);
        persisted.TenantId.Should().Be(TenantId);

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().ToListAsync());

        outbox.Should().BeEmpty("a draft is not something the Samaaj has been told about");
    }

    [Fact]
    public async Task Publishing_writes_one_outbox_row_in_the_same_transaction()
    {
        var id = await PublishedAsync(capacity: 200);

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().ToListAsync());

        outbox.Should().ContainSingle();
        outbox[0].Topic.Should().Be("events.event.published.v1");

        // The free text is the Samaaj's own copy, not something for an
        // append-only log.
        outbox[0].Payload.Should().NotContain("Paryushan");
    }

    [Fact]
    public async Task A_member_cannot_create_or_publish()
    {
        (await Member().PostAsJsonAsync("/v1/events", NewEvent()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var id = await CreateAsync();

        (await Member().PostAsync($"/v1/events/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_event_that_ends_before_it_starts_is_refused()
    {
        var response = await Organiser().PostAsJsonAsync("/v1/events", new
        {
            title = "Backwards",
            description = (string?)null,
            startAt = DateTimeOffset.UtcNow.AddDays(10),
            endAt = DateTimeOffset.UtcNow.AddDays(9),
            venue = (string?)null,
            organizerType = "Samaaj",
            organizerId = (Guid?)null,
            registrationEnabled = true,
            capacity = (int?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_capacity_of_zero_is_refused()
    {
        // An event nobody can attend is a mistake, not an intention. Leave it
        // null for no limit.
        (await Organiser().PostAsJsonAsync("/v1/events", NewEvent(capacity: 0)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_group_event_with_no_group_named_is_refused()
    {
        var response = await Organiser().PostAsJsonAsync("/v1/events", new
        {
            title = "Group event",
            description = (string?)null,
            startAt = DateTimeOffset.UtcNow.AddDays(10),
            endAt = (DateTimeOffset?)null,
            venue = (string?)null,
            organizerType = "VolunteerGroup",
            organizerId = (Guid?)null,
            registrationEnabled = true,
            capacity = (int?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Visibility -------------------------------------------------------

    [Fact]
    public async Task A_draft_is_invisible_to_members_and_visible_to_organisers()
    {
        var id = await CreateAsync();

        var memberList = await Member().GetFromJsonAsync<JsonElement>("/v1/events");
        memberList.EnumerateArray().Should().BeEmpty();

        (await Member().GetAsync($"/v1/events/{id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);

        var organiserList = await Organiser()
            .GetFromJsonAsync<JsonElement>("/v1/events?includeDrafts=true");

        organiserList.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task A_member_asking_for_drafts_gets_the_published_list_rather_than_a_refusal()
    {
        // Refusing would tell them drafts exist. The honest answer to "show me
        // everything" from a member is "here is everything you can see".
        await CreateAsync();
        await PublishedAsync();

        var list = await Member().GetFromJsonAsync<JsonElement>("/v1/events?includeDrafts=true");

        list.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task The_list_does_not_cross_Samaaj()
    {
        await PublishedAsync();

        var elsewhere = await Member(userId: Guid.NewGuid(), tenantId: OtherTenantId)
            .GetFromJsonAsync<JsonElement>("/v1/events");

        elsewhere.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task An_event_in_another_Samaaj_cannot_be_registered_for_even_with_its_id()
    {
        // The IDOR guard. Knowing the id is not access.
        var id = await PublishedAsync();

        var response = await RegisterAsync(
            Member(userId: Guid.NewGuid(), tenantId: OtherTenantId), id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Registering, capacity and the waitlist --------------------------

    [Fact]
    public async Task Registering_takes_a_place_and_shows_on_the_member_s_own_row()
    {
        var id = await PublishedAsync(capacity: 10);

        var response = await RegisterAsync(Member(), id);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("status").GetString().Should().Be("Registered");

        var list = await Member().GetFromJsonAsync<JsonElement>("/v1/events");
        var row = list.EnumerateArray().Single();

        row.GetProperty("myRegistrationStatus").GetString().Should().Be("Registered");
        row.GetProperty("registeredCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Past_capacity_a_member_is_waitlisted_and_told_where_they_stand()
    {
        // "Waitlisted" alone tells a member very little; "third in the queue"
        // is what they actually want.
        var id = await PublishedAsync(capacity: 1);

        await RegisterAsync(Member(), id);

        var second = await RegisterAsync(Member(userId: Guid.NewGuid()), id);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("status").GetString().Should().Be("Waitlisted");
        body.GetProperty("position").GetInt32().Should().Be(1);

        var third = await RegisterAsync(Member(userId: Guid.NewGuid()), id);

        (await third.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("position").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task A_full_event_says_so_on_every_row()
    {
        // What the wireframe's "Full — Waitlist" pill reads.
        var id = await PublishedAsync(capacity: 1);
        await RegisterAsync(Member(), id);

        var list = await Member(userId: Guid.NewGuid()).GetFromJsonAsync<JsonElement>("/v1/events");

        list.EnumerateArray().Single().GetProperty("isFull").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Giving_up_a_place_promotes_whoever_waited_longest()
    {
        var id = await PublishedAsync(capacity: 1);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await RegisterAsync(Member(), id);
        await RegisterAsync(Member(userId: first), id);
        await RegisterAsync(Member(userId: second), id);

        var response = await Member().DeleteAsync($"/v1/events/{id}/registration");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("promotedMemberId").GetGuid().Should().Be(first);

        var promoted = await Member(userId: first).GetFromJsonAsync<JsonElement>($"/v1/events/{id}");
        promoted.GetProperty("myRegistrationStatus").GetString().Should().Be("Registered");

        var stillWaiting = await Member(userId: second)
            .GetFromJsonAsync<JsonElement>($"/v1/events/{id}");
        stillWaiting.GetProperty("myRegistrationStatus").GetString().Should().Be("Waitlisted");
    }

    [Fact]
    public async Task Registering_twice_looks_exactly_like_registering_once()
    {
        var id = await PublishedAsync(capacity: 10);

        await RegisterAsync(Member(), id);
        var second = await RegisterAsync(Member(), id);

        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await factory.WithDbContextAsync(db =>
            db.Registrations.IgnoreQueryFilters().CountAsync(r => r.EventId == id));

        rows.Should().Be(1, "the unique index on (event, member) is what actually holds");
    }

    [Fact]
    public async Task A_cancelled_registration_reads_as_no_registration_at_all()
    {
        // The screen asks "am I going?", and somebody who cancelled is in the
        // same position as somebody who never registered.
        var id = await PublishedAsync();

        await RegisterAsync(Member(), id);
        await Member().DeleteAsync($"/v1/events/{id}/registration");

        var detail = await Member().GetFromJsonAsync<JsonElement>($"/v1/events/{id}");

        detail.GetProperty("myRegistrationStatus").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_event_that_takes_no_registrations_refuses_them()
    {
        var id = await PublishedAsync(registrationEnabled: false);

        (await RegisterAsync(Member(), id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_draft_cannot_be_registered_for_even_with_its_id()
    {
        var id = await CreateAsync();

        (await RegisterAsync(Member(), id)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Cancelling the event --------------------------------------------

    [Fact]
    public async Task Cancelling_without_a_reason_is_refused()
    {
        // Members who were going are told this, and "cancelled" alone is not an
        // answer to people who rearranged their day.
        var id = await PublishedAsync();

        var response = await Organiser().PostAsJsonAsync(
            $"/v1/events/{id}/cancel", new { reason = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cancelling_keeps_the_attendee_list_and_announces_how_many_were_affected()
    {
        var id = await PublishedAsync();
        await RegisterAsync(Member(), id);

        (await Organiser().PostAsJsonAsync(
                $"/v1/events/{id}/cancel", new { reason = "The hall is unavailable." }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var attendees = await Organiser().GetFromJsonAsync<JsonElement>($"/v1/events/{id}/attendees");
        attendees.EnumerateArray().Should().ContainSingle();

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "events.event.cancelled.v1")
                .ToListAsync());

        outbox.Should().ContainSingle();
        outbox[0].Payload.Should().NotContain("hall is unavailable");
    }

    [Fact]
    public async Task A_cancelled_event_cannot_be_republished()
    {
        var id = await PublishedAsync();

        await Organiser().PostAsJsonAsync($"/v1/events/{id}/cancel", new { reason = "Off." });

        (await Organiser().PostAsync($"/v1/events/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- The attendee list ------------------------------------------------

    [Fact]
    public async Task The_attendee_list_is_the_organiser_s_and_not_a_member_s()
    {
        // Who else is going is a fact about other people.
        var id = await PublishedAsync();
        await RegisterAsync(Member(), id);

        (await Member().GetAsync($"/v1/events/{id}/attendees"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Organiser().GetAsync($"/v1/events/{id}/attendees"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_attendee_list_puts_confirmed_places_before_the_waitlist()
    {
        var id = await PublishedAsync(capacity: 1);
        var waiting = Guid.NewGuid();

        await RegisterAsync(Member(), id);
        await RegisterAsync(Member(userId: waiting), id);

        var attendees = await Organiser().GetFromJsonAsync<JsonElement>($"/v1/events/{id}/attendees");
        var rows = attendees.EnumerateArray().ToList();

        rows.Should().HaveCount(2);
        rows[0].GetProperty("status").GetString().Should().Be("Registered");
        rows[1].GetProperty("status").GetString().Should().Be("Waitlisted");
    }
}
