using FluentAssertions;
using Sangam.Businesses.Domain.Businesses;
using Sangam.Businesses.Domain.Businesses.Events;
using Xunit;

namespace Sangam.Businesses.UnitTests;

public sealed class BusinessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OwnerMemberId = Guid.NewGuid();

    private static BusinessFields Fields() => new(
        "Shah Sweets",
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Traditional Jain sweets and snacks.",
        "9876543210",
        "shop@example.com");

    [Fact]
    public void Creating_raises_an_event_and_starts_active()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields(), Now);

        business.OwnerMemberId.Should().Be(OwnerMemberId);
        business.IsActive.Should().BeTrue();
        business.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BusinessCreatedDomainEvent>();
    }

    [Fact]
    public void Deactivating_raises_a_status_changed_event()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields(), Now);
        business.ClearDomainEvents();

        business.SetActive(false, Now.AddDays(1));

        business.IsActive.Should().BeFalse();
        business.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<BusinessStatusChangedDomainEvent>();
    }

    [Fact]
    public void Setting_the_same_status_twice_is_a_no_op()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields(), Now);
        business.ClearDomainEvents();

        business.SetActive(true, Now.AddDays(1));

        business.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Adding_a_product_makes_it_available_by_default()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields(), Now);

        var product = business.AddProduct("Kaju Katli", "500g box", 45000, Now);

        product.IsAvailable.Should().BeTrue();
        business.Products.Should().ContainSingle(p => p.Id == product.Id);
    }

    [Fact]
    public void Updating_an_unknown_product_returns_false()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields(), Now);

        var updated = business.UpdateProduct(Guid.NewGuid(), "New name", null, 100, Now);

        updated.Should().BeFalse();
    }

    [Fact]
    public void Taking_a_product_off_sale_keeps_it_on_the_business()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields(), Now);
        var product = business.AddProduct("Kaju Katli", null, 45000, Now);

        var changed = business.SetProductAvailability(product.Id, false, Now.AddHours(1));

        changed.Should().BeTrue();
        business.FindProduct(product.Id)!.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void A_blank_address_line_is_none()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields() with { AddressLine = "  Shop 4, Bapu Bazaar " }, Now);
        business.AddressLine.Should().Be("Shop 4, Bapu Bazaar");

        business.Update(Fields() with { AddressLine = "   " }, Now);
        business.AddressLine.Should().BeNull();
    }

    [Fact]
    public void Setting_a_photo_answers_the_one_it_replaces()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields(), Now);
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        business.SetPhoto(first, Now).Should().BeNull();
        business.SetPhoto(second, Now).Should().Be(first, "the caller removes it");
        business.PhotoId.Should().Be(second);

        business.RemovePhoto(Now).Should().Be(second);
        business.RemovePhoto(Now).Should().BeNull("there is nothing left to remove");
    }

    [Fact]
    public void A_product_image_is_set_and_removed_on_that_product_only()
    {
        var business = Business.Create(TenantId, OwnerMemberId, Fields(), Now);
        var product = business.AddProduct("Kaju Katli", null, 45000, Now);
        var other = business.AddProduct("Besan Ladoo", null, 30000, Now);
        var image = Guid.NewGuid();

        business.SetProductPhoto(product.Id, image, Now, out var previous).Should().BeTrue();
        previous.Should().BeNull();
        business.FindProduct(product.Id)!.PhotoId.Should().Be(image);
        business.FindProduct(other.Id)!.PhotoId.Should().BeNull();

        business.SetProductPhoto(Guid.NewGuid(), image, Now, out _).Should().BeFalse("there is no such product");

        business.RemoveProductPhoto(product.Id, Now, out var removed).Should().BeTrue();
        removed.Should().Be(image);
        business.FindProduct(product.Id)!.PhotoId.Should().BeNull();
    }
}
