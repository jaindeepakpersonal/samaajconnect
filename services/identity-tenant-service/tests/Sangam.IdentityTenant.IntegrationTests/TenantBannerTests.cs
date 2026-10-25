using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Security;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// A Samaaj's banner, end to end against a real database.
/// </summary>
/// <remarks>
/// The same storage as the logo and the opposite reading rule, so these tests
/// are mostly about the second half: a banner is a photograph, and it reaches
/// only the Samaaj it belongs to.
/// </remarks>
public sealed class TenantBannerTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string TenantsUrl = "/v1/identity/tenants";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static byte[] Jpeg(byte marker = 9) => [0xFF, 0xD8, 0xFF, 0xE0, marker, 9, 9, 9];

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private static MultipartFormDataContent Upload(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        return new MultipartFormDataContent { { content, "file", "banner.jpg" } };
    }

    private HttpClient SuperAdmin() => factory.CreateClientWith(PermissionKeys.TenantManage);

    private HttpClient MemberOf(Guid tenantId) =>
        factory.CreateClientAs(Guid.NewGuid(), tenantId, [Roles.Member], []);

    private async Task<Guid> CreateTenantAsync(string slug)
    {
        var response = await SuperAdmin().PostAsJsonAsync(TenantsUrl, new
        {
            name = "Samaaj " + slug,
            slug,
            domain = (string?)null,
            contactPerson = "Ravi Shah",
            contactEmail = "ravi@example.com",
            enabledModules = new[] { "Pathshala" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task A_member_reads_their_own_Samaajs_banner_byte_for_byte_and_privately()
    {
        var tenantId = await CreateTenantAsync("udaipur-samaaj");
        var bytes = Jpeg();

        (await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/banner", Upload(bytes)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await MemberOf(tenantId).GetAsync($"{TenantsUrl}/{tenantId}/banner");

        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        fetched.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        // A photograph of the Samaaj must not sit in a shared cache.
        fetched.Headers.CacheControl!.Private.Should().BeTrue();
        fetched.Headers.CacheControl.Public.Should().BeFalse();
    }

    [Fact]
    public async Task Nobody_outside_the_Samaaj_sees_its_banner()
    {
        var tenantId = await CreateTenantAsync("udaipur-samaaj");
        var otherId = await CreateTenantAsync("pune-samaaj");
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/banner", Upload(Jpeg()));

        (await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/banner"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Another Samaaj's member is told there is none, not that it is theirs.
        (await MemberOf(otherId).GetAsync($"{TenantsUrl}/{tenantId}/banner"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_summary_names_the_banner_path_but_never_serves_it_anonymously()
    {
        var tenantId = await CreateTenantAsync("udaipur-samaaj");
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/banner", Upload(Jpeg()));

        var summary = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>($"{TenantsUrl}/by-id/{tenantId}");

        var bannerUrl = summary.GetProperty("bannerUrl").GetString();
        bannerUrl.Should().StartWith($"{TenantsUrl}/{tenantId}/banner");

        (await factory.CreateClient().GetAsync(bannerUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await MemberOf(tenantId).GetAsync(bannerUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Replacing_a_banner_deletes_the_old_one_and_leaves_the_logo_alone()
    {
        var tenantId = await CreateTenantAsync("udaipur-samaaj");
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png()));
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/banner", Upload(Jpeg(1)));
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/banner", Upload(Jpeg(2)));

        // One logo and one banner, not three images.
        (await factory.WithDbContextAsync(db => db.TenantImages.CountAsync(i => i.TenantId == tenantId)))
            .Should().Be(2);

        (await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/logo"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var banner = await MemberOf(tenantId).GetAsync($"{TenantsUrl}/{tenantId}/banner");
        (await banner.Content.ReadAsByteArrayAsync()).Should().Equal(Jpeg(2));
    }

    [Fact]
    public async Task A_member_cannot_set_or_remove_the_banner_and_an_admin_removes_it_twice()
    {
        var tenantId = await CreateTenantAsync("udaipur-samaaj");

        (await MemberOf(tenantId).PostAsync($"{TenantsUrl}/{tenantId}/banner", Upload(Jpeg())))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/banner", Upload(Jpeg()));

        (await SuperAdmin().DeleteAsync($"{TenantsUrl}/{tenantId}/banner")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SuperAdmin().DeleteAsync($"{TenantsUrl}/{tenantId}/banner")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await MemberOf(tenantId).GetAsync($"{TenantsUrl}/{tenantId}/banner"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Something_that_is_not_an_image_is_refused_as_a_banner()
    {
        var tenantId = await CreateTenantAsync("udaipur-samaaj");

        var refused = await SuperAdmin().PostAsync(
            $"{TenantsUrl}/{tenantId}/banner", Upload("<svg onload='x()'/>"u8.ToArray()));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
