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

    // ---- Roles -----------------------------------------------------------------

    private static readonly Guid ActorId = Guid.NewGuid();

    [Fact]
    public void Every_Trust_starts_with_Trustee_which_nobody_is_elected_to()
    {
        var trust = Domain.Trusts.Trust.Create(TenantId, LocalityId, "Trust", Now);

        var trustee = trust.Roles.Should().ContainSingle().Subject;

        trustee.Should().BeSameAs(trust.TrusteeRole);
        trustee.Title.Should().Be("Trustee");
        trustee.Seats.Should().BeNull("every trustee holds it until they hold another role");
        trustee.IsElected.Should().BeFalse();
    }

    [Fact]
    public void Defining_a_role_raises_an_event_and_refuses_a_duplicate_title()
    {
        var trust = Domain.Trusts.Trust.Create(TenantId, LocalityId, "Trust", Now);
        trust.ClearDomainEvents();

        trust.DefineRole(" President ", 1, ActorId, Now, out var president).Should().Be(TrustRoleRefusal.None);

        president!.Title.Should().Be("President");
        president.IsElected.Should().BeTrue();
        trust.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TrustRoleDefinedDomainEvent>().Which.RoleId.Should().Be(president.Id);

        trust.DefineRole("president", 2, ActorId, Now, out _).Should().Be(TrustRoleRefusal.DuplicateTitle);
        trust.DefineRole("TRUSTEE", 2, ActorId, Now, out _).Should().Be(TrustRoleRefusal.DuplicateTitle);
    }

    [Fact]
    public void Trustee_cannot_be_changed_or_removed()
    {
        var trust = Domain.Trusts.Trust.Create(TenantId, LocalityId, "Trust", Now);
        var trustee = trust.TrusteeRole.Id;

        trust.UpdateRole(trustee, "Member", 5, holders: 0, ActorId, Now).Should().Be(TrustRoleRefusal.NotEditable);
        trust.RemoveRole(trustee, holders: 0, ActorId, Now).Should().Be(TrustRoleRefusal.NotEditable);
    }

    [Fact]
    public void A_role_is_never_resized_below_its_holders()
    {
        var trust = Domain.Trusts.Trust.Create(TenantId, LocalityId, "Trust", Now);
        trust.DefineRole("Secretary", 3, ActorId, Now, out var secretary);

        trust.UpdateRole(secretary!.Id, "Secretary", 1, holders: 2, ActorId, Now)
            .Should().Be(TrustRoleRefusal.SeatsBelowHolders);

        trust.UpdateRole(secretary.Id, "Joint Secretary", 2, holders: 2, ActorId, Now)
            .Should().Be(TrustRoleRefusal.None);
        secretary.Title.Should().Be("Joint Secretary");
        secretary.Seats.Should().Be(2);
    }

    [Fact]
    public void A_role_somebody_holds_is_not_removed()
    {
        var trust = Domain.Trusts.Trust.Create(TenantId, LocalityId, "Trust", Now);
        trust.DefineRole("Treasurer", 1, ActorId, Now, out var treasurer);

        trust.RemoveRole(treasurer!.Id, holders: 1, ActorId, Now).Should().Be(TrustRoleRefusal.HasHolders);
        trust.RemoveRole(treasurer.Id, holders: 0, ActorId, Now).Should().Be(TrustRoleRefusal.None);
        trust.FindRole(treasurer.Id).Should().BeNull();
    }
}
