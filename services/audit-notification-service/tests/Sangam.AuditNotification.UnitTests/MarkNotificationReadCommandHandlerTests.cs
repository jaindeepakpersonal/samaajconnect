using FluentAssertions;
using NSubstitute;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Notifications.Commands.MarkNotificationRead;
using Sangam.AuditNotification.Domain.Notifications;
using Xunit;

namespace Sangam.AuditNotification.UnitTests;

/// <summary>
/// The two checks that stop "mark my notification read" being a way to touch
/// somebody else's, and the one that stops a member claiming to have read an
/// email nobody knows they opened.
/// </summary>
public sealed class MarkNotificationReadCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly MarkNotificationReadCommandHandler _handler;

    public MarkNotificationReadCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _currentUser.UserId.Returns(UserId);
        _tenantContext.TenantId.Returns(TenantId);
        _tenantContext.RequireTenantId().Returns(TenantId);
        _notifications
            .TryRecordReadAsync(Arg.Any<NotificationRead>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _handler = new MarkNotificationReadCommandHandler(
            _notifications, _currentUser, _tenantContext, _clock);
    }

    private static Notification Notification(
        Guid? recipient,
        Guid? tenantId = null,
        NotificationChannel channel = NotificationChannel.InApp) =>
        Domain.Notifications.Notification.Create(
            tenantId ?? TenantId,
            recipient,
            "Welcome",
            "Your membership is active.",
            channel,
            Guid.NewGuid(),
            Now,
            channel == NotificationChannel.InApp ? null : "ravi@example.com");

    private Task<Application.Common.Result<MarkNotificationReadResult>> Handle(Notification notification)
    {
        _notifications
            .FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(notification);

        return _handler.Handle(new MarkNotificationReadCommand(notification.Id), CancellationToken.None);
    }

    [Fact]
    public async Task A_member_marks_their_own_notification_read()
    {
        var result = await Handle(Notification(recipient: UserId));

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyRead.Should().BeFalse();
        result.Value.ReadAt.Should().Be(Now);

        await _notifications.Received(1).TryRecordReadAsync(
            Arg.Is<NotificationRead>(r => r.UserId == UserId && r.TenantId == TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_broadcast_is_readable_by_anyone_in_the_Samaaj()
    {
        var result = await Handle(Notification(recipient: null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Another_members_notification_is_not_found()
    {
        // The IDOR guard the tenant filter cannot provide: this notification is
        // in the caller's own Samaaj, and still none of their business.
        var result = await Handle(Notification(recipient: Guid.NewGuid()));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Notification.NotFound");

        await _notifications.DidNotReceive()
            .TryRecordReadAsync(Arg.Any<NotificationRead>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Another_Samaajs_notification_is_not_found()
    {
        var result = await Handle(Notification(recipient: UserId, tenantId: OtherTenantId));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Notification.NotFound");
    }

    [Fact]
    public async Task Another_Samaajs_broadcast_is_not_found_either()
    {
        // Worth its own test: a broadcast has no recipient, so the "is it mine"
        // check passes for everyone. Only the tenant check refuses this one, and
        // without it one Samaaj's announcements would be readable by the next.
        var result = await Handle(Notification(recipient: null, tenantId: OtherTenantId));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Notification.NotFound");
    }

    [Fact]
    public async Task An_emailed_notification_cannot_be_marked_read()
    {
        // Nothing reports back that an email was opened, so a read mark on one
        // would be a claim the platform cannot support.
        var result = await Handle(Notification(recipient: UserId, channel: NotificationChannel.Email));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Notification.NotReadable");
    }

    [Fact]
    public async Task Marking_it_read_twice_is_success_not_an_error()
    {
        // Pressing it again is not a mistake to report, and an error here would
        // make a client that retries look like a client with a bug.
        var notification = Notification(recipient: UserId);
        var alreadyReadAt = Now.AddHours(-3);

        _notifications
            .TryRecordReadAsync(Arg.Any<NotificationRead>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _notifications
            .FindReadAsync(notification.Id, UserId, Arg.Any<CancellationToken>())
            .Returns(NotificationRead.Record(notification.Id, UserId, TenantId, alreadyReadAt));

        var result = await Handle(notification);

        result.IsSuccess.Should().BeTrue();
        result.Value.AlreadyRead.Should().BeTrue();
        result.Value.ReadAt.Should().Be(alreadyReadAt, "the caller asked for the state, not the write");
    }

    [Fact]
    public async Task A_notification_that_does_not_exist_is_not_found()
    {
        _notifications
            .FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Notification?)null);

        var result = await _handler.Handle(
            new MarkNotificationReadCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Notification.NotFound");
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_before_anything_is_read()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await _handler.Handle(
            new MarkNotificationReadCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Required");

        await _notifications.DidNotReceive()
            .FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
