using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Media;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Samaaj logos, end to end against a real database.
/// </summary>
/// <remarks>
/// The interesting half is who may read one. Every other image on this platform
/// is served only to people who passed a check; this one is served to anyone,
/// because the registration form draws the Samaaj directory before anybody has
/// an account. These tests are what stop that being quietly true of the write
/// path too.
/// </remarks>
public sealed class TenantLogoTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string TenantsUrl = "/v1/identity/tenants";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static byte[] Png(byte marker = 1) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 2, 3, 4];

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 9, 9, 9, 9];

    private static MultipartFormDataContent Upload(byte[] bytes, string declared = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declared);

        return new MultipartFormDataContent { { content, "file", "logo.png" } };
    }

    private HttpClient SuperAdmin() => factory.CreateClientWith(PermissionKeys.TenantManage);

    private async Task<Guid> CreateTenantAsync(string slug = "mumbai-samaaj")
    {
        var response = await SuperAdmin().PostAsJsonAsync(TenantsUrl, new
        {
            name = "Mumbai Samaaj",
            slug,
            domain = (string?)null,
            contactPerson = "Ravi Shah",
            contactEmail = "ravi@example.com",
            enabledModules = new[] { "Pathshala" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        return Guid.Parse(created!["id"].ToString()!);
    }

    // ---- The round trip -----------------------------------------------------

    [Fact]
    public async Task A_logo_is_uploaded_and_comes_back_byte_for_byte()
    {
        var tenantId = await CreateTenantAsync();
        var bytes = Png();

        var upload = await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(bytes));
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/logo");

        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        fetched.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    /// <summary>
    /// The whole point of this endpoint being anonymous. Somebody registering
    /// has no token, and the Samaaj directory they pick from is anonymous for
    /// that reason — a logo needing one could not appear beside the name.
    /// </summary>
    [Fact]
    public async Task Anybody_can_fetch_a_logo_without_signing_in()
    {
        var tenantId = await CreateTenantAsync();
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png()));

        var anonymous = await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/logo");

        anonymous.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Public rather than private, which is the opposite of a member's photo and
    /// for the same reason the endpoint is anonymous: this is an organisation's
    /// mark, so a shared cache hands it to callers who were always entitled to
    /// it.
    /// </summary>
    [Fact]
    public async Task A_logo_may_be_cached_by_anything()
    {
        var tenantId = await CreateTenantAsync();
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png()));

        var fetched = await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/logo");

        fetched.Headers.CacheControl!.Public.Should().BeTrue();
        fetched.Headers.CacheControl.Private.Should().BeFalse();
    }

    [Fact]
    public async Task The_type_served_back_is_read_from_the_bytes_not_the_upload()
    {
        var tenantId = await CreateTenantAsync();

        await SuperAdmin().PostAsync(
            $"{TenantsUrl}/{tenantId}/logo", Upload(Jpeg(), declared: "image/png"));

        var fetched = await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/logo");

        fetched.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task A_second_upload_replaces_the_first_and_leaves_one_behind()
    {
        var tenantId = await CreateTenantAsync();

        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png(marker: 1)));
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png(marker: 2)));

        var fetched = await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/logo");
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png(marker: 2));

        // The replaced row is gone rather than orphaned.
        var remaining = await factory.CountLogosAsync(tenantId);
        remaining.Should().Be(1);
    }

    [Fact]
    public async Task A_client_that_already_holds_the_logo_is_told_so()
    {
        var tenantId = await CreateTenantAsync();
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png()));

        var first = await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/logo");
        var etag = first.Headers.ETag!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, $"{TenantsUrl}/{tenantId}/logo");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var second = await factory.CreateClient().SendAsync(request);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    // ---- What is refused ----------------------------------------------------

    /// <summary>
    /// Reading a logo is anonymous; setting one is not, and the two must not be
    /// confused because they live on the same path.
    /// </summary>
    [Fact]
    public async Task Uploading_without_a_token_is_refused()
    {
        var tenantId = await CreateTenantAsync();

        var response = await factory.CreateClient()
            .PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_plain_member_cannot_set_a_logo()
    {
        var tenantId = await CreateTenantAsync();
        var member = factory.CreateClientAs(Guid.NewGuid(), [Roles.Member], []);

        var response = await member.PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_SVG_is_refused_however_it_is_labelled()
    {
        var tenantId = await CreateTenantAsync();
        var svg = Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>");

        var response = await SuperAdmin().PostAsync(
            $"{TenantsUrl}/{tenantId}/logo", Upload(svg, declared: "image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_upload_over_the_cap_is_refused()
    {
        var tenantId = await CreateTenantAsync();
        var huge = new byte[ImageContent.MaxBytes + (128 * 1024)];
        huge[0] = 0x89;
        huge[1] = 0x50;
        huge[2] = 0x4E;
        huge[3] = 0x47;

        var response = await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(huge));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_Samaaj_with_no_logo_is_a_404_and_not_an_empty_image()
    {
        var tenantId = await CreateTenantAsync();

        (await factory.CreateClient().GetAsync($"{TenantsUrl}/{tenantId}/logo"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_a_logo_twice_is_success_both_times()
    {
        var tenantId = await CreateTenantAsync();
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png()));

        (await SuperAdmin().DeleteAsync($"{TenantsUrl}/{tenantId}/logo"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SuperAdmin().DeleteAsync($"{TenantsUrl}/{tenantId}/logo"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await factory.CountLogosAsync(tenantId)).Should().Be(0);
    }

    // ---- What the directory says --------------------------------------------

    /// <summary>
    /// The wire field is still `logoUrl` and still goes into an `img src`; what
    /// changed is that it points here. It was null on every row before this,
    /// because nothing could ever set one.
    /// </summary>
    [Fact]
    public async Task The_directory_hands_out_a_path_on_this_platform()
    {
        var tenantId = await CreateTenantAsync();
        await SuperAdmin().PostAsync($"{TenantsUrl}/{tenantId}/logo", Upload(Png()));

        var summary = await factory.CreateClient()
            .GetFromJsonAsync<Dictionary<string, object?>>($"{TenantsUrl}/by-id/{tenantId}");

        summary!["logoUrl"]!.ToString()
            .Should().Be($"/v1/identity/tenants/{tenantId}/logo");
    }

    [Fact]
    public async Task A_Samaaj_with_no_logo_reports_none_at_all()
    {
        var tenantId = await CreateTenantAsync();

        var summary = await factory.CreateClient()
            .GetFromJsonAsync<Dictionary<string, object?>>($"{TenantsUrl}/by-id/{tenantId}");

        summary!["logoUrl"].Should().BeNull();
    }
}
