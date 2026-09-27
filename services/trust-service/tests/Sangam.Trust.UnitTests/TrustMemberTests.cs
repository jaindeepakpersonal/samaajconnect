using FluentAssertions;
using Sangam.Trust.Domain.Members;
using Xunit;

namespace Sangam.Trust.UnitTests;

public sealed class TrustMemberTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TrustId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid TrusteeRoleId = Guid.NewGuid();
    private static readonly Guid TreasurerRoleId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Adding_a_member_raises_an_added_event_naming_their_role()
    {
        var member = TrustMember.Create(TenantId, TrustId, MemberId, TreasurerRoleId, ActorId, Now);

        member.Status.Should().Be(TrustMemberStatus.Active);
        member.RoleId.Should().Be(TreasurerRoleId);

        var raised = member.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TrustMemberAddedDomainEvent>().Subject;

        raised.Topic.Should().Be("trust.member.added.v1");
        raised.RoleId.Should().Be(TreasurerRoleId);
    }

    [Fact]
    public void Reactivating_a_removed_member_clears_the_removal_and_raises_added_again()
    {
        var member = TrustMember.Create(TenantId, TrustId, MemberId, TrusteeRoleId, ActorId, Now);
        member.Remove(ActorId, Now);
        member.ClearDomainEvents();

        member.Reactivate(TreasurerRoleId, ActorId, Now.AddDays(1));

        member.Status.Should().Be(TrustMemberStatus.Active);
        member.RoleId.Should().Be(TreasurerRoleId);
        member.RemovedAt.Should().BeNull();
        member.RemovedBy.Should().BeNull();

        member.DomainEvents.Should().ContainSingle().Subject.Should().BeOfType<TrustMemberAddedDomainEvent>();
    }

    [Fact]
    public void Assigning_a_role_raises_an_updated_event()
    {
        var member = TrustMember.Create(TenantId, TrustId, MemberId, TrusteeRoleId, ActorId, Now);
        member.ClearDomainEvents();

        member.AssignRole(TreasurerRoleId, ActorId, Now).Should().BeTrue();

        member.RoleId.Should().Be(TreasurerRoleId);
        member.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TrustMemberUpdatedDomainEvent>().Which.RoleId.Should().Be(TreasurerRoleId);
    }

    [Fact]
    public void Assigning_the_role_already_held_changes_nothing()
    {
        var member = TrustMember.Create(TenantId, TrustId, MemberId, TreasurerRoleId, ActorId, Now);
        member.ClearDomainEvents();

        member.AssignRole(TreasurerRoleId, ActorId, Now).Should().BeTrue();
        member.DomainEvents.Should().BeEmpty("holding the role already is not a second fact");
    }

    [Fact]
    public void Assigning_a_role_to_a_removed_member_does_nothing()
    {
        var member = TrustMember.Create(TenantId, TrustId, MemberId, TrusteeRoleId, ActorId, Now);
        member.Remove(ActorId, Now);
        member.ClearDomainEvents();

        member.AssignRole(TreasurerRoleId, ActorId, Now).Should().BeFalse();
        member.RoleId.Should().Be(TrusteeRoleId, "a removed member's role does not change");
        member.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Removing_an_already_removed_member_is_a_no_op()
    {
        var member = TrustMember.Create(TenantId, TrustId, MemberId, TrusteeRoleId, ActorId, Now);
        member.Remove(ActorId, Now);
        member.ClearDomainEvents();

        member.Remove(ActorId, Now).Should().BeFalse("a repeated removal is not a second fact");
        member.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Removing_an_active_member_raises_a_removed_event()
    {
        var member = TrustMember.Create(TenantId, TrustId, MemberId, TrusteeRoleId, ActorId, Now);
        member.ClearDomainEvents();

        member.Remove(ActorId, Now).Should().BeTrue();

        var raised = member.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<TrustMemberRemovedDomainEvent>().Subject;

        raised.RemovedBy.Should().Be(ActorId);
    }
}
