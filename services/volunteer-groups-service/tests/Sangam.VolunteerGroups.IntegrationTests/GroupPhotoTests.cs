using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sangam.VolunteerGroups.Application.Security;
using Xunit;

namespace Sangam.VolunteerGroups.IntegrationTests;

/// <summary>
/// A group's banner photo: an administrator of the group sets and removes it,
/// any member of the Samaaj sees it, and nobody else does.
/// </summary>
public sealed class GroupPhotoTests(VolunteerGroupsApiFactory factory)
    : IClassFixture<VolunteerGroupsApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid Hiran = Guid.NewGuid();
    private static readonly Guid Sector14 = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantId, [Roles.Member],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsLead]);

    private HttpClient Admin(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(), tenantId ?? TenantId, [Roles.SamaajAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsManage, PermissionKeys.VolunteerGroupsLead]);

    private HttpClient AreaAdmin(Guid locality) =>
        factory.CreateClientAs(
            Guid.NewGuid(), TenantId, [Roles.Member, Roles.AreaAdmin],
            [PermissionKeys.MembersRead, PermissionKeys.VolunteerGroupsManage, PermissionKeys.VolunteerGroupsLead],
            locality);

    /// <summary>A tiny but genuine PNG signature followed by filler.</summary>
    private static byte[] Png(byte marker = 1) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 2, 3, 4, 5, 6, 7, 8];

    private static MultipartFormDataContent Upload(byte[] bytes, string declaredType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);

        return new MultipartFormDataContent { { content, "file", "photo.png" } };
    }

    private async Task<Guid> CreateGroupAsync(Guid? localityId = null)
    {
        var response = await Admin().PostAsJsonAsync("/v1/volunteer-groups/groups", new
        {
            name = "Seva Group " + Guid.NewGuid().ToString("N")[..6],
            description = "Food drives.",
            focusArea = "Social Service",
            localityId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> GroupAsync(Guid groupId) =>
        (await Member().GetFromJsonAsync<JsonElement>($"/v1/volunteer-groups/groups/{groupId}")).GetProperty("group");

    [Fact]
    public async Task An_administrator_sets_a_photo_and_every_member_sees_it_byte_for_byte()
    {
        var groupId = await CreateGroupAsync();

        (await GroupAsync(groupId)).GetProperty("photoUrl").ValueKind.Should().Be(JsonValueKind.Null);

        (await Admin().PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var photoUrl = (await GroupAsync(groupId)).GetProperty("photoUrl").GetString();
        photoUrl.Should().Be($"/v1/volunteer-groups/groups/{groupId}/photo");

        var fetched = await Member().GetAsync(photoUrl);

        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        fetched.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        fetched.Headers.CacheControl!.Private.Should().BeTrue("a group photo is often of people");
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png());
    }

    [Fact]
    public async Task Replacing_a_photo_serves_the_new_one()
    {
        var groupId = await CreateGroupAsync();

        await Admin().PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(Png(marker: 1)));
        await Admin().PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(Png(marker: 2)));

        var fetched = await Member().GetAsync($"/v1/volunteer-groups/groups/{groupId}/photo");

        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png(marker: 2));
    }

    [Fact]
    public async Task The_type_is_read_from_the_bytes_and_a_non_image_is_refused()
    {
        var groupId = await CreateGroupAsync();

        // Declared as a PNG, actually an SVG: the declaration is never trusted.
        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray();

        (await Admin().PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(svg)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await GroupAsync(groupId)).GetProperty("photoUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_member_cannot_set_or_remove_a_photo()
    {
        var groupId = await CreateGroupAsync();

        (await Member().PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await Admin().PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(Png()));

        (await Member().DeleteAsync($"/v1/volunteer-groups/groups/{groupId}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_Area_Admin_sets_a_photo_only_in_their_own_locality()
    {
        var own = await CreateGroupAsync(Hiran);
        var elsewhere = await CreateGroupAsync(Sector14);

        (await AreaAdmin(Hiran).PostAsync($"/v1/volunteer-groups/groups/{own}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AreaAdmin(Hiran).PostAsync($"/v1/volunteer-groups/groups/{elsewhere}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_Samaaj_can_neither_see_nor_set_it()
    {
        var groupId = await CreateGroupAsync();
        await Admin().PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(Png()));

        (await Member(OtherTenantId).GetAsync($"/v1/volunteer-groups/groups/{groupId}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Admin(OtherTenantId).PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(Png(marker: 9))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_takes_it_down_and_doing_it_twice_is_success()
    {
        var groupId = await CreateGroupAsync();
        await Admin().PostAsync($"/v1/volunteer-groups/groups/{groupId}/photo", Upload(Png()));

        (await Admin().DeleteAsync($"/v1/volunteer-groups/groups/{groupId}/photo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Admin().DeleteAsync($"/v1/volunteer-groups/groups/{groupId}/photo")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GroupAsync(groupId)).GetProperty("photoUrl").ValueKind.Should().Be(JsonValueKind.Null);
        (await Member().GetAsync($"/v1/volunteer-groups/groups/{groupId}/photo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
