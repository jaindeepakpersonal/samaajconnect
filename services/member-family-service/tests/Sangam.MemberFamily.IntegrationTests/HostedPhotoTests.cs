using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Domain;
using Sangam.MemberFamily.Domain.Media;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// Photos the platform hosts, end to end against a real database.
/// </summary>
/// <remarks>
/// The unit suite covers what counts as an image; this covers the two things
/// only a real request can show — that the bytes come back byte-for-byte
/// through the whole stack, and that the authorization is what it claims to be.
/// The second is the reason this feature exists at all: a store served by an
/// unguessable URL would pass a round-trip test just as well.
/// </remarks>
public sealed class HostedPhotoTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly Guid _ravi = Guid.NewGuid();
    private readonly Guid _meera = Guid.NewGuid();
    private readonly Guid _outsider = Guid.NewGuid();

    /// <summary>A tiny but genuine PNG signature followed by filler.</summary>
    private static byte[] Png(byte marker = 1) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 2, 3, 4, 5, 6, 7, 8];

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 9, 9, 9, 9];

    public async Task InitializeAsync()
    {
        await SeedAsync(_ravi, TenantA);
        await SeedAsync(_meera, TenantA);
        await SeedAsync(_outsider, TenantB);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedAsync(Guid id, Guid tenantId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        if (await db.MemberProfiles.IgnoreQueryFilters().AnyAsync(p => p.Id == id))
        {
            return;
        }

        db.MemberProfiles.Add(MemberProfile.FromRegistration(
            id, tenantId, "Member " + id.ToString("N")[..6],
            id.ToString("N") + "@example.com", DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
    }

    private HttpClient As(Guid userId, Guid tenantId, params string[] permissions) =>
        factory.CreateClientAs(
            userId, tenantId, ["Member"],
            permissions.Length == 0 ? ["Members.Read"] : permissions);

    private static MultipartFormDataContent Upload(byte[] bytes, string declaredType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);

        return new MultipartFormDataContent { { content, "file", "photo.png" } };
    }

    // ---- The round trip -----------------------------------------------------

    [Fact]
    public async Task A_member_uploads_a_photo_and_it_comes_back_byte_for_byte()
    {
        var client = As(_ravi, TenantA);
        var bytes = Png();

        var upload = await client.PostAsync($"/v1/members/{_ravi}/photo", Upload(bytes));
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await client.GetAsync($"/v1/members/{_ravi}/photo");

        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        fetched.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    /// <summary>
    /// The declared part header says PNG and the bytes are a JPEG. The stored
    /// and served type is the one read from the bytes, because the header is a
    /// string the uploader chose — which is the whole reason sniffing is not
    /// done on it.
    /// </summary>
    [Fact]
    public async Task The_type_served_back_is_read_from_the_bytes_not_from_the_upload()
    {
        var client = As(_ravi, TenantA);

        await client.PostAsync(
            $"/v1/members/{_ravi}/photo", Upload(Jpeg(), declaredType: "image/png"));

        var fetched = await client.GetAsync($"/v1/members/{_ravi}/photo");

        fetched.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task A_second_upload_replaces_the_first_and_leaves_one_image_behind()
    {
        var client = As(_meera, TenantA);

        await client.PostAsync($"/v1/members/{_meera}/photo", Upload(Png(marker: 1)));
        await client.PostAsync($"/v1/members/{_meera}/photo", Upload(Png(marker: 2)));

        var fetched = await client.GetAsync($"/v1/members/{_meera}/photo");
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png(marker: 2));

        // The replaced row is gone rather than orphaned. A photograph of
        // somebody that nothing points at has not been replaced, it has been
        // mislaid.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var kept = await db.StoredImages
            .IgnoreQueryFilters()
            .CountAsync(i => i.OwnerId == _meera);

        kept.Should().Be(1);
    }

    [Fact]
    public async Task A_browser_that_already_holds_the_photo_is_told_so_rather_than_sent_it()
    {
        var client = As(_ravi, TenantA);
        await client.PostAsync($"/v1/members/{_ravi}/photo", Upload(Png()));

        var first = await client.GetAsync($"/v1/members/{_ravi}/photo");
        var etag = first.Headers.ETag!.ToString();

        var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/members/{_ravi}/photo");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var second = await client.SendAsync(request);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await second.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// A shared cache holding these would hand them to a caller who never
    /// passed the authorization check that produced them.
    /// </summary>
    [Fact]
    public async Task A_photo_is_never_cached_by_anything_but_the_browser_that_asked()
    {
        var client = As(_ravi, TenantA);
        await client.PostAsync($"/v1/members/{_ravi}/photo", Upload(Png()));

        var fetched = await client.GetAsync($"/v1/members/{_ravi}/photo");

        fetched.Headers.CacheControl!.Private.Should().BeTrue();
        fetched.Headers.CacheControl.Public.Should().BeFalse();
    }

    // ---- What is refused ----------------------------------------------------

    [Fact]
    public async Task Something_that_is_not_an_image_is_refused()
    {
        var client = As(_ravi, TenantA);
        var svg = Encoding.UTF8.GetBytes("<svg><script>alert(1)</script></svg>");

        var response = await client.PostAsync(
            $"/v1/members/{_ravi}/photo", Upload(svg, declaredType: "image/svg+xml"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_upload_over_the_cap_is_refused_before_it_is_stored()
    {
        var client = As(_ravi, TenantA);
        var huge = new byte[ImageContent.MaxBytes + (128 * 1024)];
        huge[0] = 0x89;
        huge[1] = 0x50;
        huge[2] = 0x4E;
        huge[3] = 0x47;

        var response = await client.PostAsync($"/v1/members/{_ravi}/photo", Upload(huge));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task One_member_cannot_set_another_members_photo()
    {
        var client = As(_ravi, TenantA);

        var response = await client.PostAsync($"/v1/members/{_meera}/photo", Upload(Png()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The same rule that lets an administrator correct a member's name. Being
    /// unable to fix a photo would be an odd place to draw the line.
    /// </summary>
    [Fact]
    public async Task An_administrator_holding_members_write_can_fix_a_photo()
    {
        var client = As(_ravi, TenantA, "Members.Read", "Members.Write");

        var response = await client.PostAsync($"/v1/members/{_meera}/photo", Upload(Png()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// 404 and not 403 — the answer this platform gives to every cross-tenant
    /// read, because a 403 would confirm the id names a real member somewhere.
    /// </summary>
    [Fact]
    public async Task Another_Samaajs_member_photo_is_not_found_rather_than_forbidden()
    {
        var owner = As(_outsider, TenantB);
        await owner.PostAsync($"/v1/members/{_outsider}/photo", Upload(Png()));

        var intruder = As(_ravi, TenantA, "Members.Read", "Members.Write");

        (await intruder.GetAsync($"/v1/members/{_outsider}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await intruder.PostAsync($"/v1/members/{_outsider}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_with_no_photo_is_a_404_and_not_an_empty_image()
    {
        var client = As(_meera, TenantA);
        await client.DeleteAsync($"/v1/members/{_meera}/photo");

        (await client.GetAsync($"/v1/members/{_meera}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Removing_a_photo_twice_is_success_both_times()
    {
        var client = As(_ravi, TenantA);
        await client.PostAsync($"/v1/members/{_ravi}/photo", Upload(Png()));

        (await client.DeleteAsync($"/v1/members/{_ravi}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/v1/members/{_ravi}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // And the bytes are gone, not merely unreferenced.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        (await db.StoredImages.IgnoreQueryFilters().CountAsync(i => i.OwnerId == _ravi))
            .Should().Be(0);
    }

    // ---- Children -----------------------------------------------------------
    //
    // The reason the whole feature exists. A child's photo used to be a URL, so
    // every viewer of the record told a third-party host that a child's picture
    // had just been looked at - which is the tracking DPDP s.9(3) prohibits.

    private async Task<(Guid ChildId, Guid HeadId)> SeedChildAsync(Guid tenantId)
    {
        var headId = Guid.NewGuid();
        await SeedAsync(headId, tenantId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var family = Domain.Families.Family.Create(
            tenantId, headId, Domain.Families.Family.GenerateCode(), DateTimeOffset.UtcNow);
        db.Families.Add(family);

        var child = Domain.Children.ChildProfile.Create(
            tenantId, family.Id, "Aarav", new DateOnly(2015, 4, 2),
            Domain.Members.Gender.Male, headId, DateTimeOffset.UtcNow);
        db.ChildProfiles.Add(child);

        await db.SaveChangesAsync();

        return (child.Id, headId);
    }

    [Fact]
    public async Task A_parent_uploads_their_own_childs_photo_and_reads_it_back()
    {
        var (childId, headId) = await SeedChildAsync(TenantA);
        var client = As(headId, TenantA);

        (await client.PostAsync($"/v1/children/{childId}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var fetched = await client.GetAsync($"/v1/children/{childId}/photo");

        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png());
    }

    /// <summary>
    /// The difference from a member photo, and the one that matters. A Samaaj
    /// administrator may correct a member's own details, and a child's
    /// photograph is not administration — the same reasoning that keeps
    /// deciding a join request with the household head even for admins.
    /// </summary>
    [Fact]
    public async Task Members_write_does_not_open_somebody_elses_child()
    {
        var (childId, headId) = await SeedChildAsync(TenantA);

        // The parent uploads first, and that matters. Without a photo actually
        // there, an administrator's GET answers 404 because the child has no
        // picture rather than because they were refused - so the test would pass
        // with the family check deleted. Verified by deleting it: with this line
        // the test fails, without it it does not.
        (await As(headId, TenantA).PostAsync($"/v1/children/{childId}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = As(_ravi, TenantA, "Members.Read", "Members.Write");

        (await admin.GetAsync($"/v1/children/{childId}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await admin.PostAsync($"/v1/children/{childId}/photo", Upload(Png(marker: 9))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_Samaajs_child_photo_is_not_found()
    {
        var (childId, headId) = await SeedChildAsync(TenantB);
        await (As(headId, TenantB)).PostAsync($"/v1/children/{childId}/photo", Upload(Png()));

        (await As(_ravi, TenantA, "Members.Read", "Members.Write")
            .GetAsync($"/v1/children/{childId}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Erasure ------------------------------------------------------------

    /// <summary>
    /// Clearing the reference is not erasing the photograph. This asserts the
    /// bytes are gone from the table, because a row nothing points at is not
    /// erased — it is merely unreachable by the paths that happen to exist.
    /// </summary>
    [Fact]
    public async Task Erasing_a_member_deletes_the_photograph_and_not_just_the_link()
    {
        var memberId = Guid.NewGuid();
        await SeedAsync(memberId, TenantA);

        var client = As(memberId, TenantA);
        await client.PostAsync($"/v1/members/{memberId}/photo", Upload(Png()));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

            (await db.StoredImages.IgnoreQueryFilters().CountAsync(i => i.OwnerId == memberId))
                .Should().Be(1);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider
                .GetRequiredService<IImageStore>();
            var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

            await store.RemoveAllForOwnerAsync(TenantA, ImageOwnerKind.Member, memberId);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

            (await db.StoredImages.IgnoreQueryFilters().CountAsync(i => i.OwnerId == memberId))
                .Should().Be(0);
        }
    }

    // ---- What the directory says --------------------------------------------

    /// <summary>
    /// The wire field is still <c>photoUrl</c> and still goes straight into an
    /// <c>img src</c>; what changed is that it points at this platform. Neither
    /// portal had to learn anything new.
    /// </summary>
    [Fact]
    public async Task The_directory_hands_out_a_path_on_this_platform_and_not_a_foreign_host()
    {
        var client = As(_ravi, TenantA);
        await client.PostAsync($"/v1/members/{_ravi}/photo", Upload(Png()));

        var me = await client.GetFromJsonAsync<Dictionary<string, object>>("/v1/members/me");

        me!["photoUrl"].ToString().Should().Be($"/v1/members/{_ravi}/photo");
    }

    [Fact]
    public async Task A_member_with_no_photo_reports_no_photo_url_at_all()
    {
        var client = As(_meera, TenantA);
        await client.DeleteAsync($"/v1/members/{_meera}/photo");

        var me = await client.GetFromJsonAsync<Dictionary<string, object?>>("/v1/members/me");

        me!["photoUrl"].Should().BeNull();
    }
}
