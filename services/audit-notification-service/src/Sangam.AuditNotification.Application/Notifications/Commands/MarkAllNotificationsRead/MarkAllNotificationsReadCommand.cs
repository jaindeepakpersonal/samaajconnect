using MediatR;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Security;

namespace Sangam.AuditNotification.Application.Notifications.Commands.MarkAllNotificationsRead;

/// <summary>
/// Marks everything currently in the caller's notification list as read.
/// </summary>
/// <remarks>
/// The wireframe's "Mark all as read" button. A command rather than the client
/// looping over what it can see: a member with fifty notifications would
/// otherwise send fifty requests, and the ones that arrived after the list was
/// drawn would survive the button that claimed to clear them.
/// </remarks>
[RequiresRoles(
    Roles.SuperAdmin,
    Roles.SamaajAdmin,
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.BoliManager)]
public sealed record MarkAllNotificationsReadCommand : ICommand<MarkAllNotificationsReadResult>;

public sealed record MarkAllNotificationsReadResult(int MarkedRead);

public sealed class MarkAllNotificationsReadCommandHandler(
    INotificationRepository notifications,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<MarkAllNotificationsReadCommand, Result<MarkAllNotificationsReadResult>>
{
    public async Task<Result<MarkAllNotificationsReadResult>> Handle(
        MarkAllNotificationsReadCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<MarkAllNotificationsReadResult>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var marked = await notifications.MarkEverythingReadAsync(
            userId, tenantContext.RequireTenantId(), clock.UtcNow, cancellationToken);

        return Result.Success(new MarkAllNotificationsReadResult(marked));
    }
}
