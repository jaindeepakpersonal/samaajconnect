using MediatR;
using Sangam.AuditNotification.Api.Extensions;
using Sangam.AuditNotification.Application.AuditLogs;
using Sangam.AuditNotification.Application.AuditLogs.Queries.ListAuditLogs;
using Sangam.AuditNotification.Application.Notifications;
using Sangam.AuditNotification.Application.Notifications.Queries.GetMyNotifications;

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

        return app;
    }
}
