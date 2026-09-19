using FluentAssertions;
using Sangam.VolunteerGroups.Domain.Groups;
using Xunit;

namespace Sangam.VolunteerGroups.UnitTests;

public sealed class VolunteerGroupTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid PresidentId = Guid.NewGuid();
    private static readonly Guid ApplicantId = Guid.NewGuid();

    private static VolunteerGroup Group() =>
        VolunteerGroup.Create(
            TenantId, "  Seva Group  ", "Food drives and blood donation camps.",
            "Social Service", PresidentId, Now);

    // ---- Creation ---------------------------------------------------------

    [Fact]
    public void A_group_starts_active_with_its_president_already_in_it()
    {
        // A president who had to apply to their own group would have nobody to
        // approve the application.
        var group = Group();

        group.Status.Should().Be(GroupStatus.Active);
        group.HasMember(PresidentId).Should().BeTrue();
        group.Members.Should().ContainSingle().Which.RolePosition.Should().Be("President");
    }

    [Fact]
    public void A_group_cannot_be_created_without_a_president()
    {
        // Every request to join would queue forever with no way to tell that is
        // what was happening.
        var create = () => VolunteerGroup.Create(
            TenantId, "Orphan Group", null, null, Guid.Empty, Now);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_group_can_be_created_without_a_president_and_starts_with_no_members()
    {
        var group = VolunteerGroup.Create(TenantId, "No-president group", null, null, null, Now);

        group.PresidentMemberId.Should().BeNull();
        group.HasNoPresident.Should().BeTrue();
        group.Members.Should().BeEmpty();
        group.IsPresident(PresidentId).Should().BeFalse();
    }

    [Fact]
    public void A_president_can_be_set_on_a_group_that_has_none()
    {
        var group = VolunteerGroup.Create(TenantId, "No-president group", null, null, null, Now);

        group.ChangePresident(ApplicantId, Now).Should().BeTrue();

        group.PresidentMemberId.Should().Be(ApplicantId);
        group.Members.Should().ContainSingle().Which.RolePosition.Should().Be("President");
    }

    [Fact]
    public void Electing_the_President_position_installs_a_real_president()
    {
        // Before, only the label moved, so the winner read "President" while
        // IsPresident stayed false for them and the old president kept every power.
        var group = Group();
        var request = group.Apply(ApplicantId, null, Now)!;
        group.DecideApplication(request.Id, accepted: true, PresidentId, Now);

        group.ElectPosition("President", ApplicantId, Now).Should().BeTrue();

        group.IsPresident(ApplicantId).Should().BeTrue();
        group.IsPresident(PresidentId).Should().BeFalse();
        group.Members.Single(m => m.MemberId == PresidentId).RolePosition.Should().BeNull();
        group.Members.Single(m => m.MemberId == ApplicantId).RolePosition.Should().Be("President");
    }

    [Fact]
    public void Re_electing_the_sitting_president_changes_nothing()
    {
        var group = Group();
        group.ClearDomainEvents();

        group.ElectPosition("president", PresidentId, Now).Should().BeTrue();

        group.IsPresident(PresidentId).Should().BeTrue();
        group.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Creating_trims_the_name()
    {
        Group().Name.Should().Be("Seva Group");
    }

    // ---- Applying ---------------------------------------------------------

    [Fact]
    public void A_member_can_apply_and_the_application_starts_pending()
    {
        var group = Group();

        var application = group.Apply(ApplicantId, "Happy to help at weekends.", Now);

        application.Should().NotBeNull();
        application!.Status.Should().Be(ApplicationStatus.Pending);
    }

    [Fact]
    public void Applying_twice_leaves_one_request_for_the_president_to_decide()
    {
        var group = Group();

        group.Apply(ApplicantId, null, Now).Should().NotBeNull();

        group.Apply(ApplicantId, null, Now).Should().BeNull();
        group.Applications.Should().ContainSingle();
    }

    [Fact]
    public void A_member_already_in_the_group_cannot_apply()
    {
        var group = Group();

        group.Apply(PresidentId, null, Now).Should().BeNull();
    }

    [Fact]
    public void An_inactive_group_takes_no_new_applications()
    {
        var group = Group();
        group.ChangeStatus(GroupStatus.Inactive, Now);

        group.Apply(ApplicantId, null, Now).Should().BeNull();
    }

    [Fact]
    public void A_rejected_applicant_may_ask_again()
    {
        // Circumstances and minds both change, and a permanent bar from one
        // refusal is a heavier consequence than a president was choosing.
        var group = Group();
        var first = group.Apply(ApplicantId, null, Now)!;

        group.DecideApplication(first.Id, accepted: false, PresidentId, Now);

        var second = group.Apply(ApplicantId, null, Now.AddDays(30));

        second.Should().NotBeNull();
        group.Applications.Should().ContainSingle("the old row is replaced, not kept alongside");
    }

    // ---- Deciding ---------------------------------------------------------

    [Fact]
    public void Accepting_makes_the_applicant_a_member_with_no_position()
    {
        // Positions come from an election now, or a later manual assignment -
        // never from the accept itself.
        var group = Group();
        var application = group.Apply(ApplicantId, null, Now)!;

        group.DecideApplication(application.Id, accepted: true, PresidentId, Now)
            .Should().BeTrue();

        group.HasMember(ApplicantId).Should().BeTrue();
        group.Members.Single(m => m.MemberId == ApplicantId)
            .RolePosition.Should().BeNull();
    }

    [Fact]
    public void Rejecting_does_not()
    {
        var group = Group();
        var application = group.Apply(ApplicantId, null, Now)!;

        group.DecideApplication(application.Id, accepted: false, PresidentId, Now);

        group.HasMember(ApplicantId).Should().BeFalse();
        group.FindApplication(application.Id)!.Status.Should().Be(ApplicationStatus.Rejected);
    }

    [Fact]
    public void The_decision_keeps_who_made_it()
    {
        // "Were they ever accepted, and by whom?" needs an answer that does not
        // depend on somebody remembering.
        var group = Group();
        var application = group.Apply(ApplicantId, null, Now)!;

        group.DecideApplication(application.Id, accepted: true, PresidentId, Now);

        var decided = group.FindApplication(application.Id)!;

        decided.DecidedBy.Should().Be(PresidentId);
        decided.DecidedAt.Should().Be(Now);
    }

    [Fact]
    public void Deciding_the_same_application_twice_is_refused()
    {
        var group = Group();
        var application = group.Apply(ApplicantId, null, Now)!;

        group.DecideApplication(application.Id, accepted: true, PresidentId, Now);

        group.DecideApplication(application.Id, accepted: false, PresidentId, Now)
            .Should().BeFalse();
        group.HasMember(ApplicantId).Should().BeTrue();
    }

    [Fact]
    public void Deciding_announces_both_the_applicant_and_the_decider()
    {
        // "Who let them in?" is the first question asked when a group turns out
        // to contain somebody it should not.
        var group = Group();
        var application = group.Apply(ApplicantId, null, Now)!;
        group.ClearDomainEvents();

        group.DecideApplication(application.Id, accepted: true, PresidentId, Now);

        var raised = group.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<GroupApplicationDecidedDomainEvent>().Subject;

        raised.MemberId.Should().Be(ApplicantId);
        raised.DecidedBy.Should().Be(PresidentId);
        raised.Accepted.Should().BeTrue();
    }

    [Fact]
    public void The_application_note_never_leaves_the_group()
    {
        // It is what a member wrote about themselves, for the president who has
        // to read it - not for an append-only log.
        var group = Group();
        group.ClearDomainEvents();

        group.Apply(ApplicantId, "I have a difficult situation at home.", Now);

        var raised = group.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<GroupApplicationSubmittedDomainEvent>().Subject;

        raised.GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(nameof(GroupApplication.Note));
    }

    // ---- Positions and membership ----------------------------------------

    [Fact]
    public void A_position_can_be_given_and_cleared()
    {
        var group = Group();
        var application = group.Apply(ApplicantId, null, Now)!;
        group.DecideApplication(application.Id, accepted: true, PresidentId, Now);

        group.AssignRolePosition(ApplicantId, "  Secretary  ", Now).Should().BeTrue();
        group.Members.Single(m => m.MemberId == ApplicantId)
            .RolePosition.Should().Be("Secretary");

        group.AssignRolePosition(ApplicantId, null, Now).Should().BeTrue();
        group.Members.Single(m => m.MemberId == ApplicantId).RolePosition.Should().BeNull();
    }

    [Fact]
    public void Somebody_outside_the_group_cannot_be_given_a_position_in_it()
    {
        Group().AssignRolePosition(Guid.NewGuid(), "Secretary", Now).Should().BeFalse();
    }

    [Fact]
    public void The_president_cannot_be_removed_from_their_own_group()
    {
        // A group whose president is not in it has nobody able to decide its
        // applications. Replacing a president is its own decision.
        Group().RemoveMember(PresidentId, Guid.NewGuid(), Now).Should().BeFalse();
    }

    [Fact]
    public void An_ordinary_member_can_be_removed_and_it_announces_who_removed_them()
    {
        // The sibling of "who let them in?" - GroupApplicationDecidedDomainEvent
        // already names the president who accepted somebody; this is the same
        // question asked about the other end of a membership.
        var group = Group();
        group.Apply(ApplicantId, null, Now);
        group.DecideApplication(group.Applications.Single().Id, accepted: true, PresidentId, Now);
        group.ClearDomainEvents();

        var removed = group.RemoveMember(ApplicantId, PresidentId, Now.AddDays(1));

        removed.Should().BeTrue();
        group.HasMember(ApplicantId).Should().BeFalse();

        var raised = group.DomainEvents.OfType<GroupMemberRemovedDomainEvent>().Single();
        raised.MemberId.Should().Be(ApplicantId);
        raised.RemovedBy.Should().Be(PresidentId);
        raised.OccurredAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Removing_somebody_who_was_never_a_member_is_a_no_op()
    {
        var group = Group();
        group.ClearDomainEvents();

        group.RemoveMember(Guid.NewGuid(), PresidentId, Now).Should().BeFalse();

        group.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Removing_does_not_erase_that_they_were_ever_accepted()
    {
        // "Were they ever accepted?" stays answerable - the class doc's own
        // reasoning for keeping applications and membership as two lists.
        var group = Group();
        group.Apply(ApplicantId, null, Now);
        var application = group.Applications.Single();
        group.DecideApplication(application.Id, accepted: true, PresidentId, Now);

        group.RemoveMember(ApplicantId, PresidentId, Now.AddDays(1));

        group.Applications.Should().ContainSingle().Which.Id.Should().Be(application.Id);
    }

    [Fact]
    public void Handing_over_leaves_the_outgoing_president_in_the_group()
    {
        // Removing them would lose the group its most experienced volunteer as
        // a side effect of an administrative change.
        var group = Group();
        var successor = Guid.NewGuid();

        group.ChangePresident(successor, Now).Should().BeTrue();

        group.IsPresident(successor).Should().BeTrue();
        group.HasMember(PresidentId).Should().BeTrue();
        group.Members.Single(m => m.MemberId == PresidentId).RolePosition.Should().BeNull();
    }

    [Fact]
    public void Handing_over_announces_both_the_outgoing_and_incoming_president()
    {
        var group = Group();
        var successor = Guid.NewGuid();
        group.ClearDomainEvents();

        group.ChangePresident(successor, Now.AddDays(1));

        var raised = group.DomainEvents.OfType<GroupPresidentChangedDomainEvent>().Single();
        raised.PreviousPresidentMemberId.Should().Be(PresidentId);
        raised.PresidentMemberId.Should().Be(successor);
        raised.OccurredAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Handing_the_group_to_its_own_president_is_a_no_op()
    {
        var group = Group();
        group.ClearDomainEvents();

        group.ChangePresident(PresidentId, Now).Should().BeFalse();

        group.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_successor_id_is_refused_rather_than_clearing_the_presidency()
    {
        // A group with no president has nobody to decide its applications -
        // the same invariant VolunteerGroup.Create enforces at birth.
        var group = Group();

        group.ChangePresident(Guid.Empty, Now).Should().BeFalse();

        group.IsPresident(PresidentId).Should().BeTrue();
    }

    [Fact]
    public void Handing_the_group_to_somebody_not_yet_in_it_adds_them()
    {
        var group = Group();
        var successor = Guid.NewGuid();

        group.ChangePresident(successor, Now);

        group.HasMember(successor).Should().BeTrue();
        group.Members.Single(m => m.MemberId == successor).RolePosition.Should().Be("President");
    }

    [Fact]
    public void Handing_over_to_the_same_person_changes_nothing()
    {
        var group = Group();
        group.ClearDomainEvents();

        group.ChangePresident(PresidentId, Now).Should().BeFalse();
        group.DomainEvents.Should().BeEmpty();
    }

    // ---- Status -----------------------------------------------------------

    [Fact]
    public void Deactivating_keeps_the_members()
    {
        // Deleting the group would erase the record of who volunteered for
        // what, which is the part worth keeping.
        var group = Group();

        group.ChangeStatus(GroupStatus.Inactive, Now).Should().BeTrue();

        group.Members.Should().NotBeEmpty();
    }

    [Fact]
    public void Setting_the_status_it_already_has_raises_nothing()
    {
        var group = Group();
        group.ClearDomainEvents();

        group.ChangeStatus(GroupStatus.Active, Now).Should().BeFalse();
        group.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_status_change_announces_what_it_was_before()
    {
        var group = Group();
        group.ClearDomainEvents();

        group.ChangeStatus(GroupStatus.Inactive, Now);

        var raised = group.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<GroupStatusChangedDomainEvent>().Subject;

        raised.PreviousStatus.Should().Be(nameof(GroupStatus.Active));
        raised.Status.Should().Be(nameof(GroupStatus.Inactive));
    }

    // ---- Installing an election's winner -----------------------------------

    [Fact]
    public void Electing_installs_the_winner_in_the_position()
    {
        var group = Group();
        var application = group.Apply(ApplicantId, null, Now)!;
        group.DecideApplication(application.Id, accepted: true, PresidentId, Now);

        group.ElectPosition("Secretary", ApplicantId, Now).Should().BeTrue();

        group.Members.Single(m => m.MemberId == ApplicantId)
            .RolePosition.Should().Be("Secretary");
    }

    [Fact]
    public void Electing_clears_the_same_title_from_whoever_held_it_before()
    {
        var group = Group();
        var firstSecretary = Guid.NewGuid();
        group.DecideApplication(group.Apply(firstSecretary, null, Now)!.Id, true, PresidentId, Now);
        group.AssignRolePosition(firstSecretary, "Secretary", Now);

        var newSecretary = Guid.NewGuid();
        group.DecideApplication(group.Apply(newSecretary, null, Now)!.Id, true, PresidentId, Now);

        group.ElectPosition("Secretary", newSecretary, Now);

        group.Members.Single(m => m.MemberId == firstSecretary).RolePosition.Should().BeNull();
        group.Members.Single(m => m.MemberId == newSecretary).RolePosition.Should().Be("Secretary");
    }

    [Fact]
    public void Electing_one_position_leaves_a_different_one_alone()
    {
        var group = Group();
        var treasurer = Guid.NewGuid();
        group.DecideApplication(group.Apply(treasurer, null, Now)!.Id, true, PresidentId, Now);
        group.AssignRolePosition(treasurer, "Treasurer", Now);

        var newSecretary = Guid.NewGuid();
        group.DecideApplication(group.Apply(newSecretary, null, Now)!.Id, true, PresidentId, Now);

        group.ElectPosition("Secretary", newSecretary, Now);

        group.Members.Single(m => m.MemberId == treasurer).RolePosition.Should().Be("Treasurer");
    }

    [Fact]
    public void Electing_somebody_no_longer_in_the_group_is_refused()
    {
        var group = Group();

        group.ElectPosition("Secretary", Guid.NewGuid(), Now).Should().BeFalse();
    }
}
