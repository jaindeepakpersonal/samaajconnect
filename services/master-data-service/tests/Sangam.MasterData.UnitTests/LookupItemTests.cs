using FluentAssertions;
using Sangam.MasterData.Domain.Lookups;
using Sangam.MasterData.Domain.Lookups.Events;
using Xunit;

namespace Sangam.MasterData.UnitTests;

public sealed class LookupItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creating_a_lookup_item_raises_a_platform_wide_event()
    {
        var item = LookupItem.Create(LookupCategory.Country, "IN", "India", sortOrder: 1, Now);

        item.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<LookupItemCreatedDomainEvent>();

        item.DomainEvents.Single().TenantId.Should().Be(Guid.Empty,
            "a lookup item belongs to the platform, not to any one Samaaj");
    }

    [Fact]
    public void Updating_changes_the_label_but_never_the_code_or_category()
    {
        var item = LookupItem.Create(LookupCategory.ProfessionType, "BIZ", "Business", sortOrder: 1, Now);

        item.Update("Business Owner", sortOrder: 2, Now);

        item.Label.Should().Be("Business Owner");
        item.SortOrder.Should().Be(2);
        item.Code.Should().Be("BIZ", "other services may already hold this row's id keyed by its code");
        item.Category.Should().Be(LookupCategory.ProfessionType);
    }

    [Fact]
    public void Deactivating_twice_is_reported_as_a_no_op_the_second_time()
    {
        var item = LookupItem.Create(LookupCategory.Gender, "M", "Male", sortOrder: 1, Now);

        item.Deactivate(Now).Should().BeTrue();
        item.Deactivate(Now).Should().BeFalse(
            "the caller already achieved the outcome they asked for");

        item.IsActive.Should().BeFalse();
    }
}
