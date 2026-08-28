using MediatR;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Application.Notifications.Queries.GetMyNotifications;

public sealed class GetMyNotificationsQueryHandler(
    INotificationRepository notifications,
    ICurrentUser currentUser)
    : IRequestHandler<GetMyNotificationsQuery, Result<IReadOnlyList<NotificationResponse>>>
{
    public async Task<Result<IReadOnlyList<NotificationResponse>>> Handle(
        GetMyNotificationsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<IReadOnlyList<NotificationResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await notifications.ListForRecipientAsync(
            userId, Math.Clamp(query.Limit, 1, 200), cancellationToken);

        IReadOnlyList<NotificationResponse> results = found.Select(ToResponse).ToList();

        return Result.Success(results);
    }

    private static NotificationResponse ToResponse(Notification notification) => new(
        notification.Id,
        notification.Title,
        notification.Body,
        notification.Channel.ToString(),
        notification.Status.ToString(),
        notification.RecipientUserId is null,
        notification.CreatedAt,
        notification.ReadAt);
}
