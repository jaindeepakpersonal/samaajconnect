using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.Events.Application.Security;
using Xunit;

namespace Sangam.Events.IntegrationTests;

/// <summary>
/// An event's cover photo: an organiser sets and removes it, whoever may open
/// the event sees it, and nobody else does - a draft's photo included.
/// </summary>
public sealed class EventPhotoTests(EventsApiFactory factory)
    : IClassFixture<EventsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid Hiran = Guid.NewGuid();
    private static readonly Guid Sector14 = Guid.NewGuid();

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

    /// <summary>A tiny but genuine PNG signature followed by filler.</summary>
    private static byte[] Png(byte marker = 1) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 2, 3, 4, 5, 6, 7, 8];

    private static MultipartFormDataContent Upload(byte[] bytes, string declaredType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);

        return new MultipartFormDataContent { { content, "file", "photo.png" } };
    }

    private async Task<Guid> CreateAsync(bool publish = true, Guid? localityId = null)
    {
        var response = await Organiser().PostAsJsonAsync("/v1/events", new
        {
            title = "Paryushan Parva",
            description = (string?)null,
            startAt = DateTimeOffset.UtcNow.AddDays(30),
            endAt = (DateTimeOffset?)null,
            venue = "Upashray",
            organizerType = "Samaaj",
            organizerId = (Guid?)null,
            registrationEnabled = false,
            capacity = (int?)null,
            localityId = localityId?.ToString(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        if (publish)
        {
            (await Organiser().PostAsync($"/v1/events/{id}/publish", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return id;
    }

    [Fact]
    public async Task An_organiser_sets_a_photo_and_every_member_sees_it_byte_for_byte()
    {
        var id = await CreateAsync();

        (await Member().GetFromJsonAsync<JsonElement>($"/v1/events/{id}"))
            .GetProperty("photoUrl").ValueKind.Should().Be(JsonValueKind.Null);

        var uploaded = await Organiser().PostAsync($"/v1/events/{id}/photo", Upload(Png()));
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK);
        (await uploaded.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("photoUrl").GetString().Should().Be($"/v1/events/{id}/photo", "it answers with the whole event");

        var fetched = await Member().GetAsync($"/v1/events/{id}/photo");

        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        fetched.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        fetched.Headers.CacheControl!.Private.Should().BeTrue();
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png());
    }

    [Fact]
    public async Task Replacing_a_photo_serves_the_new_one()
    {
        var id = await CreateAsync();

        await Organiser().PostAsync($"/v1/events/{id}/photo", Upload(Png(marker: 1)));
        await Organiser().PostAsync($"/v1/events/{id}/photo", Upload(Png(marker: 2)));

        var fetched = await Member().GetAsync($"/v1/events/{id}/photo");

        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png(marker: 2));
    }

    [Fact]
    public async Task The_type_is_read_from_the_bytes_and_a_non_image_is_refused()
    {
        var id = await CreateAsync();
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

        (await Organiser().PostAsync($"/v1/events/{id}/photo", Upload(svg)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_cannot_set_one_and_a_drafts_photo_is_as_private_as_the_draft()
    {
        var draft = await CreateAsync(publish: false);

        (await Member().PostAsync($"/v1/events/{draft}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await Organiser().PostAsync($"/v1/events/{draft}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await Member().GetAsync($"/v1/events/{draft}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Organiser().GetAsync($"/v1/events/{draft}/photo")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_Area_Admin_sets_one_only_in_their_own_locality_and_another_Samaaj_sees_none()
    {
        var own = await CreateAsync(localityId: Hiran);
        var elsewhere = await CreateAsync(localityId: Sector14);

        (await AreaAdmin(Hiran).PostAsync($"/v1/events/{own}/photo", Upload(Png()))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AreaAdmin(Hiran).PostAsync($"/v1/events/{elsewhere}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Member(OtherTenantId).GetAsync($"/v1/events/{own}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_takes_it_down_and_doing_it_twice_is_success()
    {
        var id = await CreateAsync();
        await Organiser().PostAsync($"/v1/events/{id}/photo", Upload(Png()));

        (await Organiser().DeleteAsync($"/v1/events/{id}/photo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Organiser().DeleteAsync($"/v1/events/{id}/photo")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Member().GetAsync($"/v1/events/{id}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
