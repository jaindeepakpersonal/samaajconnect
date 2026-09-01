using FluentAssertions;
using Sangam.AuditNotification.Domain.Notifications;
using Xunit;

namespace Sangam.AuditNotification.UnitTests;

/// <summary>
/// The delivery state machine on <see cref="Notification"/>. This is where "how
/// many times will a member be messaged" is actually decided, so it is tested
/// here rather than only through the dispatcher.
/// </summary>
public sealed class NotificationDeliveryStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static Notification Outbound(string? destination = "ravi@example.com") =>
        Notification.Create(
            TenantId, Guid.NewGuid(), "Welcome", "Your membership is active.",
            NotificationChannel.Email, Guid.NewGuid(), Now, destination);

    private static Notification InApp() =>
        Notification.Create(
            TenantId, Guid.NewGuid(), "Welcome", "Your membership is active.",
            NotificationChannel.InApp, Guid.NewGuid(), Now);

    [Fact]
    public void An_in_app_notification_is_delivered_by_existing()
    {
        var notification = InApp();

        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.DeliveredAt.Should().Be(Now);
        notification.Destination.Should().BeNull();
    }

    [Fact]
    public void An_outbound_notification_waits_to_be_sent()
    {
        Outbound().Status.Should().Be(NotificationStatus.Pending);
    }

    [Fact]
    public void An_outbound_notification_with_no_address_fails_immediately_with_a_reason()
    {
        // Not Pending. A message with nowhere to go does not become deliverable
        // by being retried, and a Pending row that never moves is
        // indistinguishable from a stuck dispatcher.
        var notification = Outbound(destination: null);

        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.FailureReason.Should().NotBeNullOrWhiteSpace();
        notification.DeliveryAttempts.Should().Be(0);
    }

    [Fact]
    public void A_blank_address_is_treated_the_same_as_none()
    {
        Outbound(destination: "   ").Status.Should().Be(NotificationStatus.Failed);
    }

    [Fact]
    public void A_transient_failure_returns_the_message_to_the_queue()
    {
        var notification = Outbound();

        notification.RecordDeliveryFailure("Provider timed out.", permanent: false, Now);

        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.FailureReason.Should().Be("Provider timed out.");
    }

    [Fact]
    public void A_permanent_failure_stops_it_being_retried()
    {
        var notification = Outbound();

        notification.RecordDeliveryFailure("Address rejected.", permanent: true, Now);

        notification.Status.Should().Be(NotificationStatus.Failed);
    }

    [Fact]
    public void A_delivery_that_succeeds_keeps_the_record_of_the_failures_before_it()
    {
        // The only trace that a provider was down. Clearing it on success would
        // erase the evidence of the incident along with the symptom.
        var notification = Outbound();

        notification.RecordDeliveryFailure("Provider timed out.", permanent: false, Now);
        notification.MarkDelivered(Now.AddMinutes(1));

        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.DeliveredAt.Should().Be(Now.AddMinutes(1));
        notification.FailureReason.Should().Be("Provider timed out.");
    }

    [Fact]
    public void A_reason_longer_than_the_column_is_truncated_rather_than_throwing()
    {
        var notification = Outbound();

        notification.RecordDeliveryFailure(new string('x', 900), permanent: false, Now);

        notification.FailureReason!.Length.Should().Be(500);
    }

    [Fact]
    public void A_broadcast_is_addressed_to_everybody()
    {
        var broadcast = Notification.Broadcast(
            TenantId, "Paryushan schedule", "Timings for the week.",
            Guid.NewGuid(), Guid.NewGuid(), Now);

        broadcast.RecipientUserId.Should().BeNull();
        broadcast.Channel.Should().Be(NotificationChannel.InApp);
        broadcast.IsAddressedTo(Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void A_broadcast_raises_the_event_that_makes_it_auditable()
    {
        // One person putting a message in front of a whole Samaaj. Without the
        // event, the only trace would be a log line.
        var sender = Guid.NewGuid();

        var broadcast = Notification.Broadcast(
            TenantId, "Paryushan schedule", "Timings for the week.",
            Guid.NewGuid(), sender, Now);

        broadcast.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BroadcastSentDomainEvent>()
            .Which.SentBy.Should().Be(sender);
    }

    [Fact]
    public void A_notification_addressed_to_one_member_is_not_addressed_to_another()
    {
        // The half of the read guard a tenant check cannot do: inside one
        // Samaaj, somebody else's message is still not yours to touch.
        var recipient = Guid.NewGuid();

        var notification = Notification.Create(
            TenantId, recipient, "Welcome", "Your membership is active.",
            NotificationChannel.InApp, Guid.NewGuid(), Now);

        notification.IsAddressedTo(recipient).Should().BeTrue();
        notification.IsAddressedTo(Guid.NewGuid()).Should().BeFalse();
    }

    // Read state is deliberately not testable here: it is not on this aggregate
    // any more. A broadcast is one row a whole Samaaj shares, so a read flag on
    // it would have been set by the first member to open it and read for
    // everyone after. It lives in NotificationRead, one row per person per
    // message, and is exercised in the integration tests against a real unique
    // index.
}
