using System.Text.Json;
using FluentAssertions;
using Sangam.AuditNotification.Application.IntegrationEvents;
using Xunit;

namespace Sangam.AuditNotification.UnitTests;

public sealed class KnownEventsTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("identity.tenant.created.v1", "TenantCreated", "Tenant")]
    [InlineData("identity.user.registered.v1", "UserRegistered", "User")]
    [InlineData("identity.user.logged-in.v1", "UserLoggedIn", "User")]
    [InlineData("identity.tenant.status-changed.v1", "TenantStatusChanged", "Tenant")]
    public void Known_topics_map_to_their_action_and_entity(string topic, string action, string entity)
    {
        var descriptor = KnownEvents.Describe(topic);

        descriptor.Action.Should().Be(action);
        descriptor.EntityName.Should().Be(entity);
    }

    [Theory]
    [InlineData("boli.bid.placed.v1", "Placed", "Bid")]
    [InlineData("pathshala.attendance.marked.v2", "Marked", "Attendance")]
    [InlineData("timeline.post.moderation-completed.v1", "ModerationCompleted", "Post")]
    public void An_unknown_topic_still_gets_a_readable_action_rather_than_being_dropped(
        string topic, string action, string entity)
    {
        // A service this one has never been told about must still be audited.
        var descriptor = KnownEvents.Describe(topic);

        descriptor.Action.Should().Be(action);
        descriptor.EntityName.Should().Be(entity);
        descriptor.Notification.Should().BeNull();
    }

    [Fact]
    public void A_topic_with_no_version_suffix_is_handled()
    {
        KnownEvents.Describe("shop.order.placed").Action.Should().Be("Placed");
    }

    [Fact]
    public void A_nonsense_topic_does_not_throw()
    {
        var descriptor = KnownEvents.Describe("junk");

        descriptor.Action.Should().Be("Junk");
        descriptor.EntityName.Should().Be("Unknown");
    }

    [Fact]
    public void Registration_produces_a_welcome_notification_addressed_to_the_new_member()
    {
        var userId = Guid.NewGuid();

        var spec = KnownEvents.Describe("identity.user.registered.v1").Notification!(
            Payload($$"""{"userId":"{{userId}}","fullName":"Ravi Shah"}"""));

        spec.Should().NotBeNull();
        spec!.RecipientUserId.Should().Be(userId);
        spec.Body.Should().Contain("Ravi Shah");
    }

    [Fact]
    public void A_registration_payload_without_a_user_id_produces_no_notification()
    {
        // Nothing to address it to, so no notification rather than a broadcast.
        KnownEvents.Describe("identity.user.registered.v1").Notification!(Payload("""{"fullName":"Ravi"}"""))
            .Should().BeNull();
    }

    [Fact]
    public void A_registration_payload_without_a_name_still_produces_a_notification()
    {
        var userId = Guid.NewGuid();

        var spec = KnownEvents.Describe("identity.user.registered.v1").Notification!(
            Payload($$"""{"userId":"{{userId}}"}"""));

        spec.Should().NotBeNull();
        spec!.Title.Should().Be("Welcome to your Samaaj");
    }

    [Fact]
    public void Login_events_are_audited_but_do_not_notify_the_member()
    {
        KnownEvents.Describe("identity.user.logged-in.v1").Notification.Should().BeNull();
    }

    [Fact]
    public void A_status_change_names_who_changed_it_not_the_account_itself()
    {
        // The account is the entity here, not the actor - unlike
        // identity.user.logged-in.v1, suspending somebody is done *to* an
        // account, not something it did to itself. The derived default would
        // have left the actor blank, and the payload's own field is named
        // changedByUserId rather than userId for exactly this reason.
        var descriptor = KnownEvents.Describe("identity.user.status-changed.v1");

        descriptor.Action.Should().Be("UserStatusChanged");
        descriptor.EntityName.Should().Be("User");
        descriptor.EntityIdProperty.Should().Be("userId");
        descriptor.ActorIdProperty.Should().Be("changedByUserId");
        descriptor.BeforeProperties.Should().BeEquivalentTo(["previousStatus"]);
        descriptor.Notification.Should().BeNull();
    }

    [Fact]
    public void A_revoked_session_is_recorded_as_its_own_entity_with_the_account_as_actor()
    {
        // Until this descriptor existed, identity.session.revoked.v1 fell to
        // the derived default: no actor, no entity id. That is the row an
        // administrator reads to answer "who did this?" for the one reason
        // this event exists to carry - a replayed refresh token, "the closest
        // thing this platform has to an intrusion signal" per its own doc
        // comment - and the derived descriptor would have answered with a
        // blank both times.
        var descriptor = KnownEvents.Describe("identity.session.revoked.v1");

        descriptor.Action.Should().Be("SessionRevoked");
        descriptor.EntityName.Should().Be("Session");
        descriptor.EntityIdProperty.Should().Be("sessionId");
        descriptor.ActorIdProperty.Should().Be("userId");
        descriptor.Notification.Should().BeNull();
    }

    [Theory]
    [InlineData("identity.admin.invited.v1", "AdminInvited", "invitedBy")]
    [InlineData("identity.user.role-granted.v1", "RoleGranted", "grantedBy")]
    [InlineData("identity.user.role-revoked.v1", "RoleRevoked", "revokedBy")]
    public void An_administrative_event_names_the_admin_who_acted_not_its_subject(
        string topic, string action, string actorProperty)
    {
        // These are the rows read when an account turns out to have had
        // authority it should not have. Recorded by the derived defaults they
        // would carry no actor at all, which answers the question with a blank.
        var descriptor = KnownEvents.Describe(topic);

        descriptor.Action.Should().Be(action);
        descriptor.ActorIdProperty.Should().Be(actorProperty);
        descriptor.EntityIdProperty.Should().Be("userId");
    }

    // ---- volunteer-groups-service --------------------------------------
    //
    // Both GroupApplicationDecidedDomainEvent and GroupMemberRemovedDomainEvent
    // name who acted in their own doc comments - "who let them in?" and "who
    // put them out?" - and neither had a descriptor, so the derived default
    // answered both questions with a blank actor as well as a blank entity id.

    [Fact]
    public void A_group_s_creation_is_recorded_against_the_group_it_created()
    {
        var descriptor = KnownEvents.Describe("volunteer-groups.group.created.v1");

        descriptor.Action.Should().Be("GroupCreated");
        descriptor.EntityName.Should().Be("Group");
        descriptor.EntityIdProperty.Should().Be("groupId");
    }

    [Fact]
    public void An_application_is_recorded_against_itself_with_the_applicant_as_actor()
    {
        // A member applying to a group is a self-action, the same shape as
        // identity.user.logged-in.v1: nobody else did this to them.
        var descriptor = KnownEvents.Describe("volunteer-groups.application.submitted.v1");

        descriptor.EntityIdProperty.Should().Be("applicationId");
        descriptor.ActorIdProperty.Should().Be("memberId");
    }

    [Fact]
    public void A_decided_application_names_the_president_who_decided_it()
    {
        // The exact question GroupApplicationDecidedDomainEvent's own doc
        // comment exists to answer: "who let them in?"
        var descriptor = KnownEvents.Describe("volunteer-groups.application.decided.v1");

        descriptor.Action.Should().Be("ApplicationDecided");
        descriptor.EntityIdProperty.Should().Be("applicationId");
        descriptor.ActorIdProperty.Should().Be("decidedBy");
    }

    [Fact]
    public void A_removed_member_names_who_removed_them()
    {
        // The other half of the same question: "who put them out?"
        var descriptor = KnownEvents.Describe("volunteer-groups.member.removed.v1");

        descriptor.Action.Should().Be("MemberRemoved");
        descriptor.EntityName.Should().Be("GroupMember");
        descriptor.EntityIdProperty.Should().Be("memberId");
        descriptor.ActorIdProperty.Should().Be("removedBy");
    }

    [Fact]
    public void A_role_position_is_recorded_against_the_member_who_holds_it()
    {
        // The derived entity name for this topic's second segment is
        // "RolePosition", which is not a thing on this platform - a member is.
        var descriptor = KnownEvents.Describe("volunteer-groups.role-position.assigned.v1");

        descriptor.EntityName.Should().Be("GroupMember");
        descriptor.EntityIdProperty.Should().Be("memberId");
    }

    [Fact]
    public void A_changed_presidency_is_recorded_against_the_group_not_a_president_entity()
    {
        // The derived entity name for this topic's second segment is
        // "President", which does not exist as an entity on this platform -
        // it is a fact about a Group. The previous holder is kept as the
        // before-state, per SECURITY-CHECKLIST.md's before/after rule.
        var descriptor = KnownEvents.Describe("volunteer-groups.president.changed.v1");

        descriptor.EntityName.Should().Be("Group");
        descriptor.EntityIdProperty.Should().Be("groupId");
        descriptor.BeforeProperties.Should().BeEquivalentTo(["previousPresidentMemberId"]);
    }

    [Fact]
    public void A_group_status_change_keeps_its_previous_status_like_a_tenant_s_does()
    {
        var descriptor = KnownEvents.Describe("volunteer-groups.group.status-changed.v1");

        descriptor.Action.Should().Be("GroupStatusChanged");
        descriptor.EntityName.Should().Be("Group");
        descriptor.EntityIdProperty.Should().Be("groupId");
        descriptor.BeforeProperties.Should().BeEquivalentTo(["previousStatus"]);
    }

    // ---- social-issues-service ------------------------------------------
    //
    // IssueStatusChangedDomainEvent's own doc comment names two different
    // people - "the author who is waiting on the answer and the reviewer who
    // gave it" - and carries a distinct ActorUserId for exactly that reason.
    // The derived default was not merely blank here; it discarded a field the
    // event went out of its way to carry.

    [Fact]
    public void A_submitted_issue_is_recorded_against_the_member_who_raised_it()
    {
        var descriptor = KnownEvents.Describe("social-issues.issue.submitted.v1");

        descriptor.Action.Should().Be("IssueSubmitted");
        descriptor.EntityName.Should().Be("Issue");
        descriptor.EntityIdProperty.Should().Be("issueId");
        descriptor.ActorIdProperty.Should().Be("submittedByMemberId");
    }

    [Fact]
    public void An_issue_s_status_change_names_the_reviewer_not_its_author()
    {
        // SubmittedByMemberId travels on the same event, unlike a group
        // application decision, because the author is who a member portal
        // screen shows the answer to - but the actor answering "who decided
        // this?" is ActorUserId, which is what belongs on the audit row.
        var descriptor = KnownEvents.Describe("social-issues.issue.status-changed.v1");

        descriptor.Action.Should().Be("IssueStatusChanged");
        descriptor.EntityName.Should().Be("Issue");
        descriptor.EntityIdProperty.Should().Be("issueId");
        descriptor.ActorIdProperty.Should().Be("actorUserId");
        descriptor.BeforeProperties.Should().BeEquivalentTo(["previousStatus"]);
    }

    [Fact]
    public void A_published_issue_is_recorded_against_itself_with_no_actor()
    {
        // Published announces visibility rather than a decision - the decision
        // is the status change right before it, which already names who made
        // it - so there is no second actor to carry here.
        var descriptor = KnownEvents.Describe("social-issues.issue.published.v1");

        descriptor.EntityName.Should().Be("Issue");
        descriptor.EntityIdProperty.Should().Be("issueId");
        descriptor.ActorIdProperty.Should().BeNull();
    }
}
