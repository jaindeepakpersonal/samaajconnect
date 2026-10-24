using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.Competition.Application.Security;
using Xunit;

namespace Sangam.Competition.IntegrationTests;

/// <summary>
/// A competition's venue, city and photo, against a real database.
/// </summary>
public sealed class CompetitionPhotoTests(CompetitionApiFactory factory)
    : IClassFixture<CompetitionApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Organiser(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantA, [], [PermissionKeys.MembersRead, PermissionKeys.CompetitionManage]);

    private HttpClient Member() =>
        factory.CreateClientAs(Guid.NewGuid(), TenantA, [], [PermissionKeys.MembersRead]);

    /// <summary>The PNG signature and a few bytes - enough for the sniffer.</summary>
    private static byte[] Png(byte marker = 1) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 2, 3, 4, 5, 6, 7, 8];

    private static MultipartFormDataContent Upload(byte[] bytes, string declaredType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);

        return new MultipartFormDataContent { { content, "file", "photo.png" } };
    }

    private async Task<Guid> CreateAsync(string? venue = "Mahavir Bhawan, Hall 2", string? cityId = null)
    {
        var created = await Organiser().PostAsJsonAsync("/v1/competitions", new
        {
            title = "Rangoli Contest",
            description = "Annual rangoli making contest",
            competitionTypeId = (string?)null,
            registrationStartAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            registrationEndAt = DateTimeOffset.UtcNow.AddDays(7),
            localityId = Guid.NewGuid().ToString(),
            venue,
            cityId = cityId ?? Guid.NewGuid().ToString(),
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_venue_and_city_are_kept_and_corrected_with_the_rest()
    {
        var city = Guid.NewGuid().ToString();
        var id = await CreateAsync(cityId: city);

        var detail = await Member().GetFromJsonAsync<JsonElement>($"/v1/competitions/{id}");
        var competition = detail.GetProperty("competition");
        competition.GetProperty("venue").GetString().Should().Be("Mahavir Bhawan, Hall 2");
        competition.GetProperty("cityId").GetString().Should().Be(city);

        (await Organiser().PatchAsJsonAsync($"/v1/competitions/{id}", new
            {
                title = "Rangoli Contest",
                description = (string?)null,
                competitionTypeId = (string?)null,
                registrationStartAt = DateTimeOffset.UtcNow.AddMinutes(-5),
                registrationEndAt = DateTimeOffset.UtcNow.AddDays(7),
                localityId = (string?)null,
                venue = "  ",
                cityId = (string?)null,
            }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var cleared = (await Member().GetFromJsonAsync<JsonElement>($"/v1/competitions/{id}")).GetProperty("competition");
        cleared.GetProperty("venue").ValueKind.Should().Be(JsonValueKind.Null, "blank means none");
        cleared.GetProperty("cityId").ValueKind.Should().Be(JsonValueKind.Null);

        (await Organiser().PostAsJsonAsync("/v1/competitions", new
            {
                title = "Too long",
                registrationStartAt = DateTimeOffset.UtcNow,
                registrationEndAt = DateTimeOffset.UtcNow.AddDays(1),
                venue = new string('x', 301),
                cityId = "not-a-city",
            }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_organiser_uploads_a_photo_and_any_member_sees_it_on_a_new_url_each_time()
    {
        var id = await CreateAsync();

        var first = await Organiser().PostAsync($"/v1/competitions/{id}/photo", Upload(Png(1)));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstUrl = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photoUrl").GetString();
        firstUrl.Should().StartWith($"/v1/competitions/{id}/photo?v=");

        var second = await Organiser().PostAsync($"/v1/competitions/{id}/photo", Upload(Png(2)));
        var secondUrl = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photoUrl").GetString();
        secondUrl.Should().NotBe(firstUrl, "a replaced photo is a new URL");

        var fetched = await Member().GetAsync(secondUrl);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png(2));

        var listed = await Member().GetFromJsonAsync<JsonElement>("/v1/competitions");
        listed.EnumerateArray().Single().GetProperty("photoUrl").GetString().Should().Be(secondUrl);
    }

    [Fact]
    public async Task Only_an_organiser_sets_or_removes_the_photo()
    {
        var id = await CreateAsync();

        (await Member().PostAsync($"/v1/competitions/{id}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await Organiser().PostAsync($"/v1/competitions/{id}/photo", Upload(Png()));

        (await Member().DeleteAsync($"/v1/competitions/{id}/photo")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var removed = await Organiser().DeleteAsync($"/v1/competitions/{id}/photo");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photoUrl").ValueKind
            .Should().Be(JsonValueKind.Null);

        (await Organiser().DeleteAsync($"/v1/competitions/{id}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "removing twice is success");
        (await Member().GetAsync($"/v1/competitions/{id}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_photo_can_change_after_registration_closes_unlike_the_details()
    {
        var id = await CreateAsync();
        var organiser = Organiser();

        await organiser.PostAsJsonAsync($"/v1/competitions/{id}/status", new { status = "RegistrationOpen" });
        await organiser.PostAsJsonAsync($"/v1/competitions/{id}/status", new { status = "RegistrationClosed" });

        (await organiser.PostAsync($"/v1/competitions/{id}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bytes_that_are_not_an_image_are_refused_and_another_Samaaj_cannot_reach_it()
    {
        var id = await CreateAsync();

        (await Organiser().PostAsync(
                $"/v1/competitions/{id}/photo", Upload("<script>alert(1)</script>"u8.ToArray(), "image/png")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await Organiser(TenantB).PostAsync($"/v1/competitions/{id}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
