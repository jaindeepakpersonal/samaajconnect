using MediatR;
using Sangam.AuditNotification.Api.Extensions;
using Sangam.AuditNotification.Application.AuditLogs;
using Sangam.AuditNotification.Application.AuditLogs.Queries.ListAuditLogs;
using Sangam.AuditNotification.Application.Notifications;
using Sangam.AuditNotification.Application.Notifications.Commands.BroadcastNotification;
using Sangam.AuditNotification.Application.Notifications.Commands.MarkAllNotificationsRead;
using Sangam.AuditNotification.Application.Notifications.Commands.MarkNotificationRead;
using Sangam.AuditNotification.Application.Notifications.Queries.GetMyNotifications;
using Sangam.AuditNotification.Application.Notifications.Queries.ListBroadcasts;
using Sangam.AuditNotification.Application.Privacy.Queries.GetMyData;

namespace Sangam.AuditNotification.Api.Endpoints;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/audit/logs", async (
                string? action,
                string? entityName,
                int? limit,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ListAuditLogsQuery(action, entityName, limit ?? 50), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithTags("Audit")
            .WithName("ListAuditLogs")
            .WithSummary("Read this Samaaj's audit trail, newest first.")
            .Produces<IReadOnlyList<AuditLogResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/v1/audit/me/data-export", async (
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyDataQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithTags("Privacy")
            .WithName("ExportMyAuditData")
            .WithSummary("Your notifications and the actions you took (DPDP s.11).")
            .Produces<MyAuditDataResponse>();

        app.MapGet("/v1/notifications", async (
                int? limit,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetMyNotificationsQuery(limit ?? 50), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("GetMyNotifications")
            .WithSummary("The caller's notifications, plus their Samaaj's broadcasts.")
            .Produces<IReadOnlyList<NotificationResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/v1/notifications/{id:guid}/read", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new MarkNotificationReadCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("MarkNotificationRead")
            .WithSummary("Record that the caller has read one notification.")
            .Produces<MarkNotificationReadResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/v1/notifications/read-all", async (
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new MarkAllNotificationsReadCommand(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("MarkAllNotificationsRead")
            .WithSummary("Mark everything in the caller's notification list as read.")
            .Produces<MarkAllNotificationsReadResult>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        app.MapPost("/v1/notifications/broadcast", async (
                BroadcastRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new BroadcastNotificationCommand(request.Title, request.Body), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("BroadcastNotification")
            .WithSummary("Announce something to every member of this Samaaj.")
            .Produces<BroadcastNotificationResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/v1/notifications/broadcasts", async (
                int? limit,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListBroadcastsQuery(limit ?? 20), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("ListBroadcasts")
            .WithSummary("This Samaaj's announcements, newest first, with how many members opened each.")
            .Produces<IReadOnlyList<BroadcastResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// The announcement body. A record here rather than binding the command
    /// directly, so the wire shape and the command can differ: the command
    /// carries the Samaaj, and that comes from the token, never from the caller.
    /// </summary>
    public sealed record BroadcastRequest(string Title, string Body);
}
