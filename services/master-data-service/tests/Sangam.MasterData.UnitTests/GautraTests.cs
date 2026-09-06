using FluentAssertions;
using Sangam.MasterData.Domain.Gautras;
using Sangam.MasterData.Domain.Gautras.Events;
using Xunit;

namespace Sangam.MasterData.UnitTests;

public sealed class GautraTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creating_a_gautra_carries_the_owning_tenant_on_its_event()
    {
        var tenantId = Guid.NewGuid();

        var gautra = Gautra.Create(tenantId, "Kasliwal", Now);

        gautra.TenantId.Should().Be(tenantId);

        gautra.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<GautraCreatedDomainEvent>();
        gautra.DomainEvents.Single().TenantId.Should().Be(tenantId,
            "unlike the platform-wide lookups, a Gautra event belongs to a real Samaaj");
    }

    [Fact]
    public void Deactivating_an_already_inactive_gautra_changes_nothing()
    {
        var gautra = Gautra.Create(Guid.NewGuid(), "Bafna", Now);

        gautra.Deactivate(Now).Should().BeTrue();
        gautra.Deactivate(Now).Should().BeFalse();
    }
}
