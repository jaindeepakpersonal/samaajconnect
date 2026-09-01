using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Application.Notifications.Commands.BroadcastNotification;

public sealed class BroadcastNotificationCommandHandler(
    INotificationRepository notifications,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<BroadcastNotificationCommandHandler> logger)
    : IRequestHandler<BroadcastNotificationCommand, Result<BroadcastNotificationResult>>
{
    public async Task<Result<BroadcastNotificationResult>> Handle(
        BroadcastNotificationCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<BroadcastNotificationResult>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        // Never "all Samaajs". A Super Admin broadcasting has to have chosen one,
        // which they do with the override header the admin panel already sends -
        // the same mechanism every other Samaaj-scoped screen uses, and the one
        // the gateway audits. RequireTenantId is what refuses the alternative.
        var tenantId = tenantContext.RequireTenantId();

        var sentAt = clock.UtcNow;

        // A broadcast has no originating event, so it is its own source. The
        // unique index on (source_message_id, channel) therefore constrains
        // nothing here, and it should not: two identical announcements sent an
        // hour apart are two announcements. Sending the same one twice by
        // accident is a real risk, and the answer to it is the recent-broadcast
        // list this screen shows rather than a guess at how long "the same
        // message" stays the same message.
        var notification = Notification.Broadcast(
            tenantId, command.Title, command.Body, Guid.NewGuid(), actorId, sentAt);

        notifications.Add(notification);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // The audit row comes from the domain event the broadcast raised, which
        // goes out through the outbox and back in through this service's own
        // consumer. This log line is for the operator watching now.
        logger.LogInformation(
            "Samaaj {TenantId} broadcast {NotificationId} sent by {ActorId}",
            tenantId,
            notification.Id,
            actorId);

        return Result.Success(new BroadcastNotificationResult(notification.Id, sentAt));
    }
}
