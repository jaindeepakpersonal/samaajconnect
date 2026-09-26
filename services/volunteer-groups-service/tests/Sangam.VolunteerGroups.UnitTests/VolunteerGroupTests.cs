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

    private static string RoleTitleOf(VolunteerGroup group, Guid memberId) =>
        group.FindRole(group.Members.Single(m => m.MemberId == memberId).RoleId)!.Title;

    private static Guid Join(VolunteerGroup group, Guid? memberId = null)
    {
        var id = memberId ?? Guid.NewGuid();
        group.DecideApplication(group.Apply(id, null, Now)!.Id, accepted: true, PresidentId, Now);

        return id;
    }

    private static GroupRole Define(VolunteerGroup group, string title, int seats)
    {
        group.DefineRole(title, seats, PresidentId, Now, out var role).Should().Be(RoleRefusal.None);

        return role!;
    }

    // ---- Creation ---------------------------------------------------------

    [Fact]
    public void A_group_starts_active_with_its_president_already_in_it()
    {
        // A president who had to apply to their own group would have nobody to
        // approve the application.
        var group = Group();

        group.Status.Should().Be(GroupStatus.Active);
        group.HasMember(PresidentId).Should().BeTrue();
        RoleTitleOf(group, group.Members.Single().MemberId).Should().Be("President");
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
        RoleTitleOf(group, group.Members.Single().MemberId).Should().Be("President");
    }

    [Fact]
    public void Electing_the_President_role_installs_a_real_president()
    {
        // Before, only the label moved, so the winner read "President" while
        // IsPresident stayed false for them and the old president kept every power.
        var group = Group();
        Join(group, ApplicantId);

        group.ElectRole(group.PresidentRole.Id, [ApplicantId], Now).Should().BeTrue();

        group.IsPresident(ApplicantId).Should().BeTrue();
        group.IsPresident(PresidentId).Should().BeFalse();
        RoleTitleOf(group, PresidentId).Should().Be("Volunteer");
        RoleTitleOf(group, ApplicantId).Should().Be("President");
    }

    [Fact]
    public void Re_electing_the_sitting_president_changes_nothing()
    {
        var group = Group();
        group.ClearDomainEvents();

        group.ElectRole(group.PresidentRole.Id, [PresidentId], Now).Should().BeTrue();

        group.IsPresident(PresidentId).Should().BeTrue();
        group.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Every_group_is_created_with_the_two_built_in_roles()
    {
        var group = Group();

        group.Roles.Select(r => (r.Title, r.Kind, r.Seats)).Should().BeEquivalentTo(new[]
        {
            ("Volunteer", GroupRoleKind.Volunteer, (int?)null),
            ("President", GroupRoleKind.President, (int?)1),
        });
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
    public void Accepting_makes_the_applicant_a_member_in_the_Volunteer_role()
    {
        // Every member holds a role. Anything but Volunteer comes from an
        // election, or a later manual assignment - never from the accept itself.
        var group = Group();
        var application = group.Apply(ApplicantId, null, Now)!;

        group.DecideApplication(application.Id, accepted: true, PresidentId, Now)
            .Should().BeTrue();

        group.HasMember(ApplicantId).Should().BeTrue();
        RoleTitleOf(group, ApplicantId).Should().Be("Volunteer");
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

    // ---- Roles and membership --------------------------------------------

    [Fact]
    public void A_role_can_be_given_and_taken_back_to_Volunteer()
    {
        var group = Group();
        Join(group, ApplicantId);
        var secretary = Define(group, "  Secretary  ", 1);

        secretary.Title.Should().Be("Secretary");

        group.AssignRole(ApplicantId, secretary.Id, Now).Should().Be(RoleRefusal.None);
        RoleTitleOf(group, ApplicantId).Should().Be("Secretary");

        group.AssignRole(ApplicantId, group.VolunteerRole.Id, Now).Should().Be(RoleRefusal.None);
        RoleTitleOf(group, ApplicantId).Should().Be("Volunteer");
    }

    [Fact]
    public void Somebody_outside_the_group_cannot_be_given_a_role_in_it()
    {
        var group = Group();
        var secretary = Define(group, "Secretary", 1);

        group.AssignRole(Guid.NewGuid(), secretary.Id, Now).Should().Be(RoleRefusal.NotAMember);
    }

    [Fact]
    public void A_role_takes_no_more_holders_than_it_has_seats()
    {
        var group = Group();
        var coordinators = Define(group, "Coordinator", 2);
        var (a, b, c) = (Join(group), Join(group), Join(group));

        group.AssignRole(a, coordinators.Id, Now).Should().Be(RoleRefusal.None);
        group.AssignRole(b, coordinators.Id, Now).Should().Be(RoleRefusal.None);
        group.AssignRole(c, coordinators.Id, Now).Should().Be(RoleRefusal.RoleFull);

        group.HoldersOf(coordinators.Id).Should().Be(2);
    }

    [Fact]
    public void The_President_role_moves_only_with_the_presidency()
    {
        // Otherwise the title and PresidentMemberId would disagree about who
        // runs the group.
        var group = Group();
        Join(group, ApplicantId);
        var secretary = Define(group, "Secretary", 1);

        group.AssignRole(ApplicantId, group.PresidentRole.Id, Now).Should().Be(RoleRefusal.PresidencyIsSeparate);
        group.AssignRole(PresidentId, secretary.Id, Now).Should().Be(RoleRefusal.PresidencyIsSeparate);
    }

    [Fact]
    public void Two_roles_cannot_share_a_title_whatever_its_case()
    {
        var group = Group();
        Define(group, "Secretary", 1);

        group.DefineRole("secretary", 1, PresidentId, Now, out _).Should().Be(RoleRefusal.DuplicateTitle);
        group.DefineRole("VOLUNTEER", 1, PresidentId, Now, out _).Should().Be(RoleRefusal.DuplicateTitle);
    }

    [Fact]
    public void The_built_in_roles_cannot_be_changed_or_removed()
    {
        var group = Group();

        group.UpdateRole(group.VolunteerRole.Id, "Helper", 5, PresidentId, Now).Should().Be(RoleRefusal.NotEditable);
        group.UpdateRole(group.PresidentRole.Id, "Chair", 2, PresidentId, Now).Should().Be(RoleRefusal.NotEditable);
        group.RemoveRole(group.PresidentRole.Id, PresidentId, Now).Should().Be(RoleRefusal.NotEditable);
    }

    [Fact]
    public void A_role_cannot_shrink_below_its_holders_or_be_removed_while_held()
    {
        var group = Group();
        var coordinators = Define(group, "Coordinator", 3);
        group.AssignRole(Join(group), coordinators.Id, Now);
        group.AssignRole(Join(group), coordinators.Id, Now);

        group.UpdateRole(coordinators.Id, "Coordinator", 1, PresidentId, Now).Should().Be(RoleRefusal.SeatsBelowHolders);
        group.RemoveRole(coordinators.Id, PresidentId, Now).Should().Be(RoleRefusal.HasHolders);

        group.UpdateRole(coordinators.Id, "Co-ordinator", 2, PresidentId, Now).Should().Be(RoleRefusal.None);
        group.FindRole(coordinators.Id)!.Title.Should().Be("Co-ordinator");
    }

    [Fact]
    public void Defining_changing_and_removing_a_role_each_name_who_did_it()
    {
        var group = Group();
        group.ClearDomainEvents();

        var role = Define(group, "Treasurer", 1);
        group.UpdateRole(role.Id, "Treasurer", 2, PresidentId, Now);
        group.RemoveRole(role.Id, PresidentId, Now).Should().Be(RoleRefusal.None);

        group.DomainEvents.OfType<GroupRoleDefinedDomainEvent>().Single().ChangedBy.Should().Be(PresidentId);
        group.DomainEvents.OfType<GroupRoleUpdatedDomainEvent>().Single().ChangedBy.Should().Be(PresidentId);
        group.DomainEvents.OfType<GroupRoleRemovedDomainEvent>().Single().RoleId.Should().Be(role.Id);
        group.FindRole(role.Id).Should().BeNull();
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
        RoleTitleOf(group, PresidentId).Should().Be("Volunteer");
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
        RoleTitleOf(group, successor).Should().Be("President");
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

    // ---- Updating -----------------------------------------------------------

    [Fact]
    public void Updating_trims_the_name_and_clears_blank_optional_fields()
    {
        var group = Group();

        group.Update("  Renamed Seva Group  ", "   ", "", Now);

        group.Name.Should().Be("Renamed Seva Group");
        group.Description.Should().BeNull();
        group.FocusArea.Should().BeNull();
    }

    [Fact]
    public void Updating_touches_neither_the_president_nor_the_status()
    {
        var group = Group();

        group.Update("Renamed", "New description", "New focus", Now);

        group.PresidentMemberId.Should().Be(PresidentId);
        group.Status.Should().Be(GroupStatus.Active);
    }

    [Fact]
    public void Updating_announces_the_correction()
    {
        var group = Group();
        group.ClearDomainEvents();

        group.Update("Renamed", "New description", "New focus", Now);

        group.DomainEvents.Should().ContainSingle().Subject.Should().BeOfType<GroupUpdatedDomainEvent>();
    }

    // ---- Installing an election's winners ----------------------------------

    [Fact]
    public void Electing_installs_the_winner_in_the_role()
    {
        var group = Group();
        Join(group, ApplicantId);
        var secretary = Define(group, "Secretary", 1);

        group.ElectRole(secretary.Id, [ApplicantId], Now).Should().BeTrue();

        RoleTitleOf(group, ApplicantId).Should().Be("Secretary");
    }

    [Fact]
    public void Electing_sends_whoever_held_the_role_and_did_not_win_back_to_Volunteer()
    {
        var group = Group();
        var secretary = Define(group, "Secretary", 1);
        var firstSecretary = Join(group);
        group.AssignRole(firstSecretary, secretary.Id, Now);
        var newSecretary = Join(group);

        group.ElectRole(secretary.Id, [newSecretary], Now);

        RoleTitleOf(group, firstSecretary).Should().Be("Volunteer");
        RoleTitleOf(group, newSecretary).Should().Be("Secretary");
    }

    [Fact]
    public void Electing_every_seat_of_a_multi_seat_role_at_once()
    {
        var group = Group();
        var coordinators = Define(group, "Coordinator", 3);
        var kept = Join(group);
        var dropped = Join(group);
        group.AssignRole(kept, coordinators.Id, Now);
        group.AssignRole(dropped, coordinators.Id, Now);
        var (new1, new2) = (Join(group), Join(group));

        group.ElectRole(coordinators.Id, [kept, new1, new2], Now).Should().BeTrue();

        group.Members.Where(m => m.RoleId == coordinators.Id).Select(m => m.MemberId)
            .Should().BeEquivalentTo([kept, new1, new2]);
        RoleTitleOf(group, dropped).Should().Be("Volunteer");
    }

    [Fact]
    public void Electing_one_role_leaves_a_different_one_alone()
    {
        var group = Group();
        var treasurerRole = Define(group, "Treasurer", 1);
        var secretary = Define(group, "Secretary", 1);
        var treasurer = Join(group);
        group.AssignRole(treasurer, treasurerRole.Id, Now);

        group.ElectRole(secretary.Id, [Join(group)], Now);

        RoleTitleOf(group, treasurer).Should().Be("Treasurer");
    }

    [Fact]
    public void Electing_somebody_no_longer_in_the_group_changes_nothing()
    {
        var group = Group();
        var secretary = Define(group, "Secretary", 1);
        var holder = Join(group);
        group.AssignRole(holder, secretary.Id, Now);

        group.ElectRole(secretary.Id, [Guid.NewGuid()], Now).Should().BeFalse();

        RoleTitleOf(group, holder).Should().Be("Secretary", "a refused result is not half applied");
    }

    [Fact]
    public void The_president_cannot_be_elected_into_another_role()
    {
        var group = Group();
        var secretary = Define(group, "Secretary", 1);

        group.ElectRole(secretary.Id, [PresidentId], Now).Should().BeFalse();

        group.IsPresident(PresidentId).Should().BeTrue();
        RoleTitleOf(group, PresidentId).Should().Be("President");
    }

    [Fact]
    public void Volunteer_has_no_election()
    {
        var group = Group();

        group.ElectRole(group.VolunteerRole.Id, [Join(group)], Now).Should().BeFalse();
    }
}
