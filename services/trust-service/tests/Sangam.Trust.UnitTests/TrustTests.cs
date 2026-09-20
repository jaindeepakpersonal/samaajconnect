using FluentAssertions;
using Sangam.Trust.Domain.Trusts;
using Xunit;

namespace Sangam.Trust.UnitTests;

public sealed class TrustTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LocalityId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creating_a_trust_raises_a_created_event()
    {
        var trust = Domain.Trusts.Trust.Create(TenantId, LocalityId, " Hiran Magri Trust ", Now);

        trust.Name.Should().Be("Hiran Magri Trust", "the name is trimmed");
        trust.Status.Should().Be(TrustStatus.Active);

        var raised = trust.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TrustCreatedDomainEvent>().Subject;

        raised.Topic.Should().Be("trust.trust.created.v1");
        raised.LocalityId.Should().Be(LocalityId);
    }

    [Fact]
    public void Changing_to_the_same_status_is_a_no_op()
    {
        var trust = Domain.Trusts.Trust.Create(TenantId, LocalityId, "Trust", Now);
        trust.ClearDomainEvents();

        trust.ChangeStatus(TrustStatus.Active, Now).Should().BeFalse("it is already Active");
        trust.DomainEvents.Should().BeEmpty("a no-op is not a second fact");
    }

    [Fact]
    public void Changing_status_raises_an_event_carrying_both_statuses()
    {
        var trust = Domain.Trusts.Trust.Create(TenantId, LocalityId, "Trust", Now);
        trust.ClearDomainEvents();

        trust.ChangeStatus(TrustStatus.Inactive, Now).Should().BeTrue();

        var raised = trust.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TrustStatusChangedDomainEvent>().Subject;

        raised.PreviousStatus.Should().Be("Active");
        raised.Status.Should().Be("Inactive");
    }
}
