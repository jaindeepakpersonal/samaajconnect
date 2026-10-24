using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Businesses.Application.IntegrationEvents;
using Sangam.Businesses.Application.IntegrationEvents.Commands.EraseBusinessesData;
using Sangam.Businesses.Application.Security;
using Xunit;

namespace Sangam.Businesses.IntegrationTests;

/// <summary>
/// A business's location line, its photo and its products' images, against a
/// real database - the cascade that takes images with an erased owner's
/// businesses is a foreign key, which nothing substituted would hold.
/// </summary>
public sealed class BusinessPhotoTests(BusinessesApiFactory factory)
    : IClassFixture<BusinessesApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Buyer = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid userId) =>
        factory.CreateClientAs(userId, TenantA, [Roles.Member], [PermissionKeys.MembersRead]);

    /// <summary>
    /// The PNG signature and a few bytes after it - enough for the sniffer,
    /// which reads the format from the leading bytes. <paramref name="marker"/>
    /// tells two uploads apart.
    /// </summary>
    private static byte[] Png(byte marker = 1) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker, 2, 3, 4, 5, 6, 7, 8];

    private static MultipartFormDataContent Upload(byte[] bytes, string declaredType = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);

        return new MultipartFormDataContent { { content, "file", "photo.png" } };
    }

    private async Task<Guid> CreateBusinessAsync()
    {
        var response = await Member(Owner).PostAsJsonAsync("/v1/businesses/mine", new
        {
            name = "Shah Sweets",
            categoryId = Guid.NewGuid().ToString(),
            cityId = Guid.NewGuid().ToString(),
            localityId = Guid.NewGuid().ToString(),
            addressLine = "Shop 4, Bapu Bazaar",
            description = "Traditional Jain sweets.",
            contactPhone = "9876543210",
            contactEmail = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddProductAsync(Guid businessId, string name = "Kaju Katli")
    {
        var response = await Member(Owner).PostAsJsonAsync(
            $"/v1/businesses/mine/{businessId}/products", new { name, description = "500g box", priceInPaise = 45000 });

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("products").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_address_line_is_kept_and_corrected_with_the_rest_of_the_business()
    {
        var businessId = await CreateBusinessAsync();

        var created = await Member(Buyer).GetFromJsonAsync<JsonElement>($"/v1/businesses/{businessId}");
        created.GetProperty("addressLine").GetString().Should().Be("Shop 4, Bapu Bazaar");

        (await Member(Owner).PatchAsJsonAsync($"/v1/businesses/mine/{businessId}", new
            {
                name = "Shah Sweets",
                categoryId = created.GetProperty("categoryId").GetGuid().ToString(),
                cityId = (string?)null,
                localityId = (string?)null,
                addressLine = "  ",
                description = (string?)null,
                contactPhone = (string?)null,
                contactEmail = (string?)null,
            }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var cleared = await Member(Buyer).GetFromJsonAsync<JsonElement>($"/v1/businesses/{businessId}");
        cleared.GetProperty("addressLine").ValueKind.Should().Be(JsonValueKind.Null, "blank means none");

        (await Member(Owner).PostAsJsonAsync("/v1/businesses/mine", new
            {
                name = "Too long",
                categoryId = Guid.NewGuid().ToString(),
                addressLine = new string('x', 301),
            }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_owner_uploads_a_photo_and_any_member_of_the_Samaaj_sees_it()
    {
        var businessId = await CreateBusinessAsync();

        var uploaded = await Member(Owner).PostAsync($"/v1/businesses/mine/{businessId}/photo", Upload(Png()));
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK);

        var photoUrl = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photoUrl").GetString();
        photoUrl.Should().StartWith($"/v1/businesses/{businessId}/photo?v=", "a replaced photo is a new URL");

        var fetched = await Member(Buyer).GetAsync(photoUrl);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        fetched.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await fetched.Content.ReadAsByteArrayAsync()).Should().Equal(Png());

        // The directory carries it too, so a listing can show it.
        var directory = await Member(Buyer).GetFromJsonAsync<JsonElement>("/v1/businesses");
        directory.EnumerateArray().Single().GetProperty("photoUrl").GetString().Should().Be(photoUrl);
    }

    [Fact]
    public async Task Replacing_a_photo_leaves_one_and_a_new_url()
    {
        var businessId = await CreateBusinessAsync();

        var first = (await (await Member(Owner).PostAsync($"/v1/businesses/mine/{businessId}/photo", Upload(Png(1))))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photoUrl").GetString();
        var second = (await (await Member(Owner).PostAsync($"/v1/businesses/mine/{businessId}/photo", Upload(Png(2))))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photoUrl").GetString();

        second.Should().NotBe(first);
        (await (await Member(Buyer).GetAsync(second)).Content.ReadAsByteArrayAsync()).Should().Equal(Png(2));

        var count = await factory.WithDbContextAsync(db =>
            db.BusinessImages.IgnoreQueryFilters().CountAsync(i => i.BusinessId == businessId));
        count.Should().Be(1, "the replaced photo goes in the same transaction");
    }

    [Fact]
    public async Task Only_the_owner_may_set_or_remove_a_photo()
    {
        var businessId = await CreateBusinessAsync();

        (await Member(Buyer).PostAsync($"/v1/businesses/mine/{businessId}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "whether a business is somebody's is theirs to say");

        await Member(Owner).PostAsync($"/v1/businesses/mine/{businessId}/photo", Upload(Png()));

        (await Member(Buyer).DeleteAsync($"/v1/businesses/mine/{businessId}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var removed = await Member(Owner).DeleteAsync($"/v1/businesses/mine/{businessId}/photo");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("photoUrl").ValueKind
            .Should().Be(JsonValueKind.Null);

        (await Member(Owner).DeleteAsync($"/v1/businesses/mine/{businessId}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.OK, "removing twice is success");
        (await Member(Buyer).GetAsync($"/v1/businesses/{businessId}/photo"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bytes_that_are_not_an_image_are_refused_whatever_they_claim_to_be()
    {
        var businessId = await CreateBusinessAsync();

        (await Member(Owner).PostAsync(
                $"/v1/businesses/mine/{businessId}/photo", Upload("<script>alert(1)</script>"u8.ToArray(), "image/png")))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await Member(Owner).PostAsync(
                $"/v1/businesses/mine/{businessId}/photo", Upload(new byte[3 * 1024 * 1024])))
            .StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_product_image_is_shown_while_it_is_on_sale_and_to_the_owner_always()
    {
        var businessId = await CreateBusinessAsync();
        var productId = await AddProductAsync(businessId);

        var uploaded = await Member(Owner).PostAsync(
            $"/v1/businesses/mine/{businessId}/products/{productId}/photo", Upload(Png(7)));
        uploaded.StatusCode.Should().Be(HttpStatusCode.OK);

        var product = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("products")
            .EnumerateArray().Single();
        var url = product.GetProperty("photoUrl").GetString();
        url.Should().StartWith($"/v1/businesses/{businessId}/products/{productId}/photo?v=");

        (await (await Member(Buyer).GetAsync(url)).Content.ReadAsByteArrayAsync()).Should().Equal(Png(7));

        await Member(Owner).PostAsJsonAsync(
            $"/v1/businesses/mine/{businessId}/products/{productId}/availability", new { available = false });

        (await Member(Buyer).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, "the product is hidden from them");
        (await Member(Owner).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Member(Owner).PostAsync(
                $"/v1/businesses/mine/{businessId}/products/{Guid.NewGuid()}/photo", Upload(Png())))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var removed = await Member(Owner).DeleteAsync($"/v1/businesses/mine/{businessId}/products/{productId}/photo");
        (await removed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("products").EnumerateArray().Single()
            .GetProperty("photoUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Erasing_an_owner_takes_every_image_of_their_businesses_with_them()
    {
        var businessId = await CreateBusinessAsync();
        var productId = await AddProductAsync(businessId);
        await Member(Owner).PostAsync($"/v1/businesses/mine/{businessId}/photo", Upload(Png(1)));
        await Member(Owner).PostAsync($"/v1/businesses/mine/{businessId}/products/{productId}/photo", Upload(Png(2)));

        var envelope = new IntegrationEventEnvelope(
            Guid.NewGuid(), TenantA, "identity.user.erased.v1", "UserErasedDomainEvent",
            $$"""{"userId":"{{Owner}}","tenantId":"{{TenantA}}"}""", DateTimeOffset.UtcNow);

        (await factory.SendAsync(new EraseBusinessesDataCommand(envelope))).IsSuccess.Should().BeTrue();

        var left = await factory.WithDbContextAsync(db =>
            db.BusinessImages.IgnoreQueryFilters().CountAsync(i => i.BusinessId == businessId));
        left.Should().Be(0);
    }
}
