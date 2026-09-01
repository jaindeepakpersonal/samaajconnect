using MediatR;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Application.Notifications.Commands.MarkNotificationRead;

public sealed class MarkNotificationReadCommandHandler(
    INotificationRepository notifications,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<MarkNotificationReadCommand, Result<MarkNotificationReadResult>>
{
    public async Task<Result<MarkNotificationReadResult>> Handle(
        MarkNotificationReadCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<MarkNotificationReadResult>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var tenantId = tenantContext.RequireTenantId();

        var notification = await notifications.FindByIdAsync(command.NotificationId, cancellationToken);

        // Re-validated against the tenant context rather than trusting the query
        // filter (CLAUDE.md §6). Not found and wrong-Samaaj answer identically,
        // so a member cannot learn that a notification id exists somewhere else
        // by the shape of the refusal.
        if (notification is null || notification.TenantId != tenantId)
        {
            return Result.Failure<MarkNotificationReadResult>(
                Error.NotFound("Notification.NotFound", "No such notification in this Samaaj."));
        }

        // The second half of the guard, and the one a tenant check alone would
        // miss: inside a Samaaj, a notification addressed to another member is
        // still not this caller's to touch.
        if (!notification.IsAddressedTo(userId))
        {
            return Result.Failure<MarkNotificationReadResult>(
                Error.NotFound("Notification.NotFound", "No such notification in this Samaaj."));
        }

        if (notification.Channel != NotificationChannel.InApp)
        {
            // Nothing reports back that an email was opened, so a read mark on
            // one would be a claim the platform cannot support. These are not
            // returned by the notification list either.
            return Result.Failure<MarkNotificationReadResult>(Error.Conflict(
                "Notification.NotReadable",
                "Only in-app notifications can be marked read. Whether a message sent by email "
                + "or text was opened is not something this platform knows."));
        }

        var readAt = clock.UtcNow;

        // Insert-or-nothing in one statement rather than "look, then write".
        // Opening a notification twice in quick succession is ordinary - two
        // tabs, a double tap, a client that retries - and a check followed by an
        // insert would let both attempts past the check and leave the unique
        // index to turn the second into a 500. See the repository.
        var recorded = await notifications.TryRecordReadAsync(
            NotificationRead.Record(notification.Id, userId, tenantId, readAt), cancellationToken);

        if (recorded)
        {
            // No SaveChanges. The insert is a statement, not a tracked change,
            // and it runs inside the transaction TransactionBehavior opened for
            // this command - which commits it on success and rolls it back on
            // failure like any other write. A SaveChanges here would have
            // nothing to save and would suggest to the next reader that it did.
            return Result.Success(new MarkNotificationReadResult(notification.Id, readAt, AlreadyRead: false));
        }

        // Already read, so report when - the caller asked for the state, not for
        // the write. Falls back to now only if the row vanished between the two
        // statements, which needs an erasure landing mid-request.
        var existing = await notifications.FindReadAsync(notification.Id, userId, cancellationToken);

        return Result.Success(
            new MarkNotificationReadResult(notification.Id, existing?.ReadAt ?? readAt, AlreadyRead: true));
    }
}
