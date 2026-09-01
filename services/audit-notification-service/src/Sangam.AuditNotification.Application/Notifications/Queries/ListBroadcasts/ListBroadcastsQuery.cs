using MediatR;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Security;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Application.Notifications.Queries.ListBroadcasts;

/// <summary>
/// This Samaaj's announcements, newest first — the wireframe's "Recent
/// Notifications" card.
/// </summary>
/// <remarks>
/// Not decoration. Nothing stops an administrator sending the same announcement
/// twice, and deliberately so: two identical messages an hour apart are two
/// messages, and any rule that guessed otherwise would eventually swallow one
/// somebody meant to send. Showing what has already gone out is the thing that
/// makes the duplicate visible before it is sent rather than after.
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.NotificationsBroadcast)]
public sealed record ListBroadcastsQuery(int Limit = 20) : IQuery<IReadOnlyList<BroadcastResponse>>;

/// <param name="ReadCount">
/// How many members have opened it. The wireframe's Status column says
/// "Delivered", which for an in-app announcement is true the moment the row
/// exists and so says nothing. This is the number that does.
/// </param>
public sealed record BroadcastResponse(
    Guid Id,
    string Title,
    string Body,
    DateTimeOffset SentAt,
    int ReadCount);

public sealed class ListBroadcastsQueryHandler(INotificationRepository notifications)
    : IRequestHandler<ListBroadcastsQuery, Result<IReadOnlyList<BroadcastResponse>>>
{
    public async Task<Result<IReadOnlyList<BroadcastResponse>>> Handle(
        ListBroadcastsQuery query,
        CancellationToken cancellationToken)
    {
        var found = await notifications.ListBroadcastsAsync(
            Math.Clamp(query.Limit, 1, 100), cancellationToken);

        return Result.Success(found);
    }
}
