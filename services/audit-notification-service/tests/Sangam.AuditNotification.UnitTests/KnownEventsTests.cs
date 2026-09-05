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
}
