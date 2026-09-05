using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.IntegrationEvents;
using Sangam.AuditNotification.Application.IntegrationEvents.Commands.RecordIntegrationEvent;
using Sangam.AuditNotification.Domain.AuditLogs;
using Sangam.AuditNotification.Domain.Notifications;
using Xunit;

namespace Sangam.AuditNotification.UnitTests;

public sealed class RecordIntegrationEventCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly IAuditLogRepository _auditLogs = Substitute.For<IAuditLogRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly RecordIntegrationEventCommandHandler _handler;

    public RecordIntegrationEventCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);

        _handler = new RecordIntegrationEventCommandHandler(
            _auditLogs,
            _notifications,
            _unitOfWork,
            _clock,
            NullLogger<RecordIntegrationEventCommandHandler>.Instance);
    }

    private static IntegrationEventEnvelope Envelope(
        string topic = "identity.user.registered.v1",
        string? payload = null,
        Guid? messageId = null) =>
        new(
            messageId ?? Guid.NewGuid(),
            TenantId,
            topic,
            "Sangam.IdentityTenant.Domain.Users.UserRegisteredDomainEvent",
            payload ?? $$"""{"userId":"{{UserId}}","tenantId":"{{TenantId}}","fullName":"Ravi Shah"}""",
            Now.AddMinutes(-1));

    /// <summary>
    /// A registration payload carrying the identifier the member signed up with,
    /// which is what decides whether the welcome can also leave the platform.
    /// </summary>
    private static string PayloadWith(string mobileOrEmail) =>
        $$"""
          {"userId":"{{UserId}}","tenantId":"{{TenantId}}","fullName":"Ravi Shah",
           "mobileOrEmail":"{{mobileOrEmail}}"}
          """;

    private Task<Application.Common.Result<RecordIntegrationEventResult>> Handle(
        IntegrationEventEnvelope envelope) =>
        _handler.Handle(new RecordIntegrationEventCommand(envelope), CancellationToken.None);

    [Fact]
    public async Task Records_an_audit_row_for_the_event()
    {
        var envelope = Envelope();

        var result = await Handle(envelope);

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyRecorded.Should().BeFalse();

        _auditLogs.Received(1).Add(Arg.Is<AuditLog>(a =>
            a.TenantId == TenantId
            && a.SourceMessageId == envelope.MessageId
            && a.Action == "UserRegistered"
            && a.EntityName == "User"));

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stamps_the_event_time_from_the_publisher_and_the_write_time_from_the_clock()
    {
        var envelope = Envelope();

        await Handle(envelope);

        _auditLogs.Received(1).Add(Arg.Is<AuditLog>(a =>
            a.OccurredAt == envelope.OccurredAt && a.RecordedAt == Now));
    }

    [Fact]
    public async Task Extracts_the_actor_and_entity_id_from_the_payload()
    {
        await Handle(Envelope());

        _auditLogs.Received(1).Add(Arg.Is<AuditLog>(a =>
            a.ActorUserId == UserId && a.EntityId == UserId.ToString()));
    }

    [Fact]
    public async Task A_removed_group_member_is_audited_against_who_removed_them()
    {
        // volunteer-groups.member.removed.v1 had no descriptor at all until
        // this cycle - the derived default would have left both the entity
        // id and the actor blank on the one row meant to answer "who put
        // them out?"
        var memberId = Guid.NewGuid();
        var removedBy = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        await Handle(Envelope(
            topic: "volunteer-groups.member.removed.v1",
            payload: $$"""
                {"groupId":"{{groupId}}","tenantId":"{{TenantId}}",
                 "memberId":"{{memberId}}","removedBy":"{{removedBy}}"}
                """));

        _auditLogs.Received(1).Add(Arg.Is<AuditLog>(a =>
            a.Action == "MemberRemoved"
            && a.EntityName == "GroupMember"
            && a.EntityId == memberId.ToString()
            && a.ActorUserId == removedBy));
    }

    [Fact]
    public async Task A_decided_issue_is_audited_against_the_reviewer_not_its_author()
    {
        // social-issues.issue.status-changed.v1 had no descriptor at all until
        // this cycle, and its own event carries a distinct ActorUserId
        // precisely so the reviewer who decided it, not the member who
        // submitted it, lands on the audit row.
        var issueId = Guid.NewGuid();
        var submittedBy = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();

        await Handle(Envelope(
            topic: "social-issues.issue.status-changed.v1",
            payload: $$"""
                {"issueId":"{{issueId}}","tenantId":"{{TenantId}}",
                 "submittedByMemberId":"{{submittedBy}}","actorUserId":"{{reviewerId}}",
                 "previousStatus":"UnderReview","status":"Published"}
                """));

        _auditLogs.Received(1).Add(Arg.Is<AuditLog>(a =>
            a.Action == "IssueStatusChanged"
            && a.EntityName == "Issue"
            && a.EntityId == issueId.ToString()
            && a.ActorUserId == reviewerId
            && a.BeforeState == "{\"previousStatus\":\"UnderReview\"}"));
    }

    [Fact]
    public async Task A_redelivered_event_is_skipped_rather_than_recorded_twice()
    {
        _auditLogs.AlreadyRecordedAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handle(Envelope());

        // At-least-once delivery makes this a normal outcome, not a failure.
        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyRecorded.Should().BeTrue();

        _auditLogs.DidNotReceive().Add(Arg.Any<AuditLog>());
        _notifications.DidNotReceive().Add(Arg.Any<Notification>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Registration_also_raises_a_welcome_notification()
    {
        var result = await Handle(Envelope());

        result.Value.NotificationRaised.Should().BeTrue();

        _notifications.Received(1).Add(Arg.Is<Notification>(n =>
            n.RecipientUserId == UserId && n.TenantId == TenantId));
    }

    [Fact]
    public async Task A_login_event_is_audited_without_notifying_anyone()
    {
        var result = await Handle(Envelope(topic: "identity.user.logged-in.v1"));

        result.Value.NotificationRaised.Should().BeFalse();

        _auditLogs.Received(1).Add(Arg.Any<AuditLog>());
        _notifications.DidNotReceive().Add(Arg.Any<Notification>());
    }

    [Fact]
    public async Task A_notification_already_raised_for_this_message_is_not_raised_again()
    {
        _notifications
            .AlreadyRaisedAsync(Arg.Any<Guid>(), Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await Handle(Envelope());

        result.Value.NotificationRaised.Should().BeFalse();
        _notifications.DidNotReceive().Add(Arg.Any<Notification>());
    }

    [Fact]
    public async Task Registration_with_an_email_identifier_also_queues_an_emailed_copy()
    {
        await Handle(Envelope(payload: PayloadWith("ravi.shah@example.com")));

        _notifications.Received(1).Add(Arg.Is<Notification>(n =>
            n.Channel == NotificationChannel.InApp));

        _notifications.Received(1).Add(Arg.Is<Notification>(n =>
            n.Channel == NotificationChannel.Email
            && n.Destination == "ravi.shah@example.com"
            && n.Status == NotificationStatus.Pending));
    }

    [Fact]
    public async Task Registration_with_a_mobile_identifier_queues_it_as_a_text_message()
    {
        await Handle(Envelope(payload: PayloadWith("+919876543210")));

        _notifications.Received(1).Add(Arg.Is<Notification>(n =>
            n.Channel == NotificationChannel.Sms && n.Destination == "+919876543210"));
    }

    [Fact]
    public async Task An_identifier_that_is_neither_raises_the_in_app_notification_only()
    {
        // Refusing the event would stall the partition behind one member whose
        // identifier the platform cannot send to. They still get told in-app.
        await Handle(Envelope(payload: PayloadWith("ravi-shah")));

        _notifications.Received(1).Add(Arg.Any<Notification>());
        _notifications.Received(1).Add(Arg.Is<Notification>(n =>
            n.Channel == NotificationChannel.InApp));
    }

    [Fact]
    public async Task An_emailed_copy_already_raised_for_this_message_is_not_raised_again()
    {
        // The dedupe is per channel: the in-app row is new, the emailed one is
        // a redelivery, and only the first should be written.
        _notifications
            .AlreadyRaisedAsync(Arg.Any<Guid>(), NotificationChannel.Email, Arg.Any<CancellationToken>())
            .Returns(true);

        await Handle(Envelope(payload: PayloadWith("ravi.shah@example.com")));

        _notifications.Received(1).Add(Arg.Any<Notification>());
        _notifications.DidNotReceive().Add(Arg.Is<Notification>(n =>
            n.Channel == NotificationChannel.Email));
    }

    [Fact]
    public async Task An_unparseable_payload_is_still_audited_verbatim()
    {
        // Losing the record of an event because its body is malformed would put
        // a hole in the trail; the raw text is kept instead.
        var result = await Handle(Envelope(payload: "not json at all"));

        result.IsSuccess.Should().BeTrue();

        _auditLogs.Received(1).Add(Arg.Is<AuditLog>(a =>
            a.AfterState == "not json at all" && a.ActorUserId == null));
    }

    [Fact]
    public async Task An_event_from_an_unknown_service_is_recorded_with_a_derived_action()
    {
        await Handle(Envelope(topic: "boli.bid.placed.v1", payload: "{}"));

        _auditLogs.Received(1).Add(Arg.Is<AuditLog>(a =>
            a.Action == "Placed" && a.EntityName == "Bid"));
    }
}
