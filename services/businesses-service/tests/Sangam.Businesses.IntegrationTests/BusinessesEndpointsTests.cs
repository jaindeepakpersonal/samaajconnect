using System.Net;
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
/// Businesses, products and orders through their endpoints, against a real
/// database. The tenant query filter is applied by the DbContext rather than
/// by any handler, and the outbox guarantee is transactional - neither can be
/// shown against a substituted repository.
/// </summary>
public sealed class BusinessesEndpointsTests(BusinessesApiFactory factory)
    : IClassFixture<BusinessesApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Buyer = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid userId, Guid? tenantId = null) =>
        factory.CreateClientAs(userId, tenantId ?? TenantA, [Roles.Member], [PermissionKeys.MembersRead]);

    private static object CreateBusinessBody(
        string name = "Shah Sweets",
        string? categoryId = null,
        string? cityId = null,
        string? localityId = null) => new
    {
        name,
        categoryId = categoryId ?? Guid.NewGuid().ToString(),
        cityId,
        localityId,
        description = "Traditional Jain sweets and snacks.",
        contactPhone = "9876543210",
        contactEmail = "shop@example.com",
    };

    private async Task<Guid> CreateBusinessAsync(
        Guid ownerId, Guid? tenantId = null, string name = "Shah Sweets",
        string? categoryId = null, string? localityId = null)
    {
        var response = await Member(ownerId, tenantId)
            .PostAsJsonAsync("/v1/businesses/mine", CreateBusinessBody(name, categoryId, localityId: localityId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();

        return created.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddProductAsync(
        Guid ownerId, Guid businessId, string name = "Kaju Katli", int priceInPaise = 45000)
    {
        var response = await Member(ownerId).PostAsJsonAsync(
            $"/v1/businesses/mine/{businessId}/products", new { name, description = "500g box", priceInPaise });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();

        return updated.GetProperty("products").EnumerateArray()
            .Single(p => p.GetProperty("name").GetString() == name)
            .GetProperty("id").GetGuid();
    }

    // ---- Businesses ---------------------------------------------------------

    [Fact]
    public async Task Creating_a_business_round_trips_every_field()
    {
        var businessId = await CreateBusinessAsync(Owner);

        var mine = await Member(Owner).GetFromJsonAsync<JsonElement>($"/v1/businesses/{businessId}");

        mine.GetProperty("name").GetString().Should().Be("Shah Sweets");
        mine.GetProperty("contactPhone").GetString().Should().Be("9876543210");
        mine.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task An_invalid_category_is_a_validation_problem_not_a_crash()
    {
        var response = await Member(Owner)
            .PostAsJsonAsync("/v1/businesses/mine", CreateBusinessBody(categoryId: "null"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Deactivating_a_business_removes_it_from_the_directory_but_keeps_it_for_the_owner()
    {
        var businessId = await CreateBusinessAsync(Owner);

        (await Member(Owner).PostAsJsonAsync($"/v1/businesses/mine/{businessId}/status", new { active = false }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var directory = await Member(Buyer).GetFromJsonAsync<JsonElement>("/v1/businesses");
        directory.EnumerateArray().Should().NotContain(b => b.GetProperty("id").GetGuid() == businessId);

        var ownersView = await Member(Owner).GetFromJsonAsync<JsonElement>($"/v1/businesses/{businessId}");
        ownersView.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Only_the_owner_may_edit_a_business()
    {
        var businessId = await CreateBusinessAsync(Owner);

        var response = await Member(Stranger).PatchAsJsonAsync(
            $"/v1/businesses/mine/{businessId}", CreateBusinessBody("Renamed"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_business_in_another_Samaaj_is_not_found()
    {
        var businessId = await CreateBusinessAsync(Owner, TenantB);

        var response = await Member(Buyer, TenantA).GetAsync($"/v1/businesses/{businessId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Directory search ---------------------------------------------------

    [Fact]
    public async Task Search_finds_an_active_business_and_excludes_an_inactive_one()
    {
        await CreateBusinessAsync(Owner, name: "Shah Sweets");
        var inactive = await CreateBusinessAsync(Owner, name: "Closed Shop");
        await Member(Owner).PostAsJsonAsync($"/v1/businesses/mine/{inactive}/status", new { active = false });

        var results = await Member(Buyer).GetFromJsonAsync<JsonElement>("/v1/businesses");
        var names = results.EnumerateArray().Select(b => b.GetProperty("name").GetString()).ToList();

        names.Should().Contain("Shah Sweets");
        names.Should().NotContain("Closed Shop");
    }

    [Fact]
    public async Task Search_filters_by_category()
    {
        var categoryId = Guid.NewGuid().ToString();
        await CreateBusinessAsync(Owner, name: "Matches", categoryId: categoryId);
        await CreateBusinessAsync(Owner, name: "Does not match");

        var results = await Member(Buyer)
            .GetFromJsonAsync<JsonElement>($"/v1/businesses?categoryId={categoryId}");

        var names = results.EnumerateArray().Select(b => b.GetProperty("name").GetString()).ToList();

        names.Should().ContainSingle().Which.Should().Be("Matches");
    }

    [Fact]
    public async Task An_unavailable_product_is_hidden_from_a_non_owner()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId);

        await Member(Owner).PostAsJsonAsync(
            $"/v1/businesses/mine/{businessId}/products/{productId}/availability", new { available = false });

        var asOwner = await Member(Owner).GetFromJsonAsync<JsonElement>($"/v1/businesses/{businessId}");
        asOwner.GetProperty("products").GetArrayLength().Should().Be(1);

        var asBuyer = await Member(Buyer).GetFromJsonAsync<JsonElement>($"/v1/businesses/{businessId}");
        asBuyer.GetProperty("products").GetArrayLength().Should().Be(0);
    }

    // ---- Orders ---------------------------------------------------------------

    [Fact]
    public async Task A_member_cannot_order_from_their_own_business()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId);

        var response = await Member(Owner).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 1 } },
            note = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_order_against_an_unavailable_product_is_refused()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId);
        await Member(Owner).PostAsJsonAsync(
            $"/v1/businesses/mine/{businessId}/products/{productId}/availability", new { available = false });

        var response = await Member(Buyer).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 1 } },
            note = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_order_can_be_placed_accepted_and_fulfilled()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId, priceInPaise: 45000);

        var placeResponse = await Member(Buyer).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 2 } },
            note = "Please pack well.",
        });
        placeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var placed = await placeResponse.Content.ReadFromJsonAsync<JsonElement>();
        placed.GetProperty("totalAmountInPaise").GetInt32().Should().Be(90000);
        var orderId = placed.GetProperty("id").GetGuid();

        var acceptResponse = await Member(Owner).PostAsJsonAsync(
            $"/v1/businesses/orders/{orderId}/respond", new { accept = true });
        acceptResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fulfillResponse = await Member(Owner).PostAsync($"/v1/businesses/orders/{orderId}/fulfill", null);
        fulfillResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var fulfilled = await fulfillResponse.Content.ReadFromJsonAsync<JsonElement>();
        fulfilled.GetProperty("status").GetString().Should().Be("Fulfilled");

        var received = await Member(Owner).GetFromJsonAsync<JsonElement>("/v1/businesses/orders/received");
        received.EnumerateArray().Single().GetProperty("businessName").GetString().Should().Be("Shah Sweets");
    }

    [Fact]
    public async Task Only_the_owner_may_respond_to_an_order()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId);

        var placeResponse = await Member(Buyer).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 1 } },
            note = (string?)null,
        });
        var orderId = (await placeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await Member(Buyer).PostAsJsonAsync(
            $"/v1/businesses/orders/{orderId}/respond", new { accept = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_buyer_can_cancel_a_pending_order()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId);

        var placeResponse = await Member(Buyer).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 1 } },
            note = (string?)null,
        });
        var orderId = (await placeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var cancelResponse = await Member(Buyer).PostAsync($"/v1/businesses/orders/{orderId}/cancel", null);
        cancelResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await Member(Buyer).GetFromJsonAsync<JsonElement>("/v1/businesses/orders/mine");
        mine.EnumerateArray().Single().GetProperty("status").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task The_buyer_cannot_cancel_an_accepted_order()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId);

        var placeResponse = await Member(Buyer).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 1 } },
            note = (string?)null,
        });
        var orderId = (await placeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await Member(Owner).PostAsJsonAsync($"/v1/businesses/orders/{orderId}/respond", new { accept = true });

        var response = await Member(Buyer).PostAsync($"/v1/businesses/orders/{orderId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_owner_can_cancel_an_accepted_order()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId);

        var placeResponse = await Member(Buyer).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 1 } },
            note = (string?)null,
        });
        var orderId = (await placeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await Member(Owner).PostAsJsonAsync($"/v1/businesses/orders/{orderId}/respond", new { accept = true });

        var response = await Member(Owner).PostAsync($"/v1/businesses/orders/{orderId}/cancel", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---- Tenant isolation -----------------------------------------------------

    [Fact]
    public async Task A_member_of_another_Samaaj_cannot_order_from_a_business_here()
    {
        var businessId = await CreateBusinessAsync(Owner, TenantA);
        var productId = await AddProductAsync(Owner, businessId);

        var response = await Member(Stranger, TenantB).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 1 } },
            note = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Erasure ----------------------------------------------------------------

    [Fact]
    public async Task Account_erasure_removes_owned_businesses_and_de_identifies_placed_orders()
    {
        var businessId = await CreateBusinessAsync(Owner);
        var productId = await AddProductAsync(Owner, businessId);

        var placeResponse = await Member(Buyer).PostAsJsonAsync("/v1/businesses/orders", new
        {
            businessId,
            items = new[] { new { productId = productId.ToString(), quantity = 1 } },
            note = "A private note.",
        });
        var orderId = (await placeResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var envelope = new IntegrationEventEnvelope(
            Guid.NewGuid(), TenantA, "identity.user.erased.v1", "UserErasedDomainEvent",
            $$"""{"userId":"{{Buyer}}","tenantId":"{{TenantA}}"}""", DateTimeOffset.UtcNow);

        var result = await factory.SendAsync(new EraseBusinessesDataCommand(envelope));

        result.IsSuccess.Should().BeTrue();

        var order = await factory.WithDbContextAsync(db =>
            db.Orders.IgnoreQueryFilters().SingleAsync(o => o.Id == orderId));
        order.Note.Should().BeNull();
        order.Status.Should().Be(Domain.Orders.OrderStatus.Pending);

        // The business owner's own received-order history survives the buyer's erasure.
        var received = await Member(Owner).GetFromJsonAsync<JsonElement>("/v1/businesses/orders/received");
        received.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Account_erasure_removes_every_business_the_member_owned()
    {
        var businessId = await CreateBusinessAsync(Owner);

        var envelope = new IntegrationEventEnvelope(
            Guid.NewGuid(), TenantA, "identity.user.erased.v1", "UserErasedDomainEvent",
            $$"""{"userId":"{{Owner}}","tenantId":"{{TenantA}}"}""", DateTimeOffset.UtcNow);

        var result = await factory.SendAsync(new EraseBusinessesDataCommand(envelope));

        result.IsSuccess.Should().BeTrue();

        var stillThere = await factory.WithDbContextAsync(db =>
            db.Businesses.IgnoreQueryFilters().AnyAsync(b => b.Id == businessId));
        stillThere.Should().BeFalse();
    }
}
