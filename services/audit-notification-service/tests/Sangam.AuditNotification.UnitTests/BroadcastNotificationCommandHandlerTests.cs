using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Notifications.Commands.BroadcastNotification;
using Sangam.AuditNotification.Domain.Notifications;
using Xunit;

namespace Sangam.AuditNotification.UnitTests;

public sealed class BroadcastNotificationCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly BroadcastNotificationCommandHandler _handler;

    public BroadcastNotificationCommandHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
        _currentUser.UserId.Returns(ActorId);
        _tenantContext.RequireTenantId().Returns(TenantId);

        _handler = new BroadcastNotificationCommandHandler(
            _notifications,
            _unitOfWork,
            _currentUser,
            _tenantContext,
            _clock,
            NullLogger<BroadcastNotificationCommandHandler>.Instance);
    }

    private Task<Application.Common.Result<BroadcastNotificationResult>> Handle(
        string title = "Paryushan schedule", string body = "Timings for the week.") =>
        _handler.Handle(new BroadcastNotificationCommand(title, body), CancellationToken.None);

    [Fact]
    public async Task It_writes_one_notification_addressed_to_nobody_in_particular()
    {
        // One row, not one per member. A Samaaj of two thousand would otherwise
        // mean two thousand copies of the same sentence, and erasing one member
        // would have to find theirs among them.
        var result = await Handle();

        result.IsSuccess.Should().BeTrue();

        _notifications.Received(1).Add(Arg.Is<Notification>(n =>
            n.RecipientUserId == null
            && n.TenantId == TenantId
            && n.Channel == NotificationChannel.InApp
            && n.Title == "Paryushan schedule"));
    }

    [Fact]
    public async Task It_is_in_app_because_this_service_holds_no_directory_to_email()
    {
        await Handle();

        _notifications.Received(1).Add(Arg.Is<Notification>(n =>
            n.Channel == NotificationChannel.InApp && n.Destination == null));
    }

    [Fact]
    public async Task It_raises_the_event_that_puts_an_actor_on_the_audit_row()
    {
        await Handle();

        _notifications.Received(1).Add(Arg.Is<Notification>(n =>
            n.DomainEvents.OfType<BroadcastSentDomainEvent>().Single().SentBy == ActorId));
    }

    [Fact]
    public async Task A_Super_Admin_with_no_Samaaj_chosen_cannot_broadcast_to_all_of_them()
    {
        // RequireTenantId is what refuses it. A broadcast with no tenant would
        // be a write reaching every Samaaj on the platform, which nothing else
        // here does and which should not arrive as a side effect of forgetting
        // to pick one.
        _tenantContext.RequireTenantId()
            .Returns(_ => throw new InvalidOperationException("No Samaaj resolved."));

        var act = async () => await Handle();

        await act.Should().ThrowAsync<InvalidOperationException>();

        _notifications.DidNotReceive().Add(Arg.Any<Notification>());
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_refused()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await Handle();

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Required");
    }
}

public sealed class BroadcastNotificationCommandValidatorTests
{
    private readonly BroadcastNotificationCommandValidator _validator = new();

    [Theory]
    [InlineData("", "Timings for the week.")]
    [InlineData("   ", "Timings for the week.")]
    [InlineData("Paryushan schedule", "")]
    [InlineData("Paryushan schedule", "   ")]
    public void An_announcement_needs_both_a_title_and_something_to_say(string title, string body) =>
        _validator.Validate(new BroadcastNotificationCommand(title, body))
            .IsValid.Should().BeFalse();

    [Fact]
    public void The_limits_match_the_columns_so_a_long_message_is_a_message_not_a_500()
    {
        _validator.Validate(new BroadcastNotificationCommand(new string('x', 201), "Fine"))
            .IsValid.Should().BeFalse();

        _validator.Validate(new BroadcastNotificationCommand("Fine", new string('x', 2001)))
            .IsValid.Should().BeFalse();

        _validator.Validate(new BroadcastNotificationCommand(new string('x', 200), new string('x', 2000)))
            .IsValid.Should().BeTrue();
    }
}
