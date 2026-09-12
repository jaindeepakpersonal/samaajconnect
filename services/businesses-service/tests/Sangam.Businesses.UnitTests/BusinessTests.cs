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
}
