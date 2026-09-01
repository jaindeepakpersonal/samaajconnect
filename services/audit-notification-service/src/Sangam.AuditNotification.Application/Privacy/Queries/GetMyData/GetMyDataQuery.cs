using MediatR;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Notifications;
using Sangam.AuditNotification.Application.Security;
using Sangam.AuditNotification.Domain.Notifications;

namespace Sangam.AuditNotification.Application.Privacy.Queries.GetMyData;

/// <summary>
/// Everything audit-notification-service holds about the caller.
/// </summary>
/// <remarks>
/// DPDP section 11. Per-service by design; see docs/product/DPDP-COMPLIANCE.md.
///
/// The audit rows here are the ones where this member is the *actor* - things
/// they did. Rows about actions others took that merely mention them are not
/// included: an audit log is largely a record of administrators' work, and
/// handing someone else's actions to a member on request would turn a
/// transparency right into a surveillance tool.
/// </remarks>
[RequiresRoles(
    Roles.Member,
    Roles.FamilyHead,
    Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher,
    Roles.PathshalaStudent,
    Roles.ContentModerator,
    Roles.SamaajAdmin,
    Roles.SuperAdmin)]
public sealed record GetMyDataQuery : IQuery<MyAuditDataResponse>;

public sealed record MyAuditDataResponse(
    string ExportedAt,
    string Service,
    IReadOnlyList<NotificationResponse> Notifications,
    IReadOnlyList<MyActionResponse> ActionsYouTook,
    IReadOnlyList<string> ProcessingPurposes,
    IReadOnlyList<string> HeldElsewhere);

/// <summary>
/// One audit row, without the payload. The payload is the state of whatever
/// was changed, which may be someone else's data.
/// </summary>
public sealed record MyActionResponse(
    string Action,
    string EntityName,
    string? EntityId,
    DateTimeOffset OccurredAt);

public sealed class GetMyDataQueryHandler(
    INotificationRepository notifications,
    IAuditLogQueries auditLogs,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetMyDataQuery, Result<MyAuditDataResponse>>
{
    public async Task<Result<MyAuditDataResponse>> Handle(
        GetMyDataQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<MyAuditDataResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        // Every channel, not just in-app. The member notification list filters to
        // in-app so an emailed copy does not read as a second message; an export
        // of what this service holds about someone must not hide that a message
        // was also sent to their address, or which address.
        var mine = await notifications.ListEveryChannelForRecipientAsync(
            userId, 500, cancellationToken);
        var actions = await auditLogs.ListForActorAsync(userId, 500, cancellationToken);

        return Result.Success(new MyAuditDataResponse(
            clock.UtcNow.ToString("O"),
            "audit-notification-service",
            mine.Select(ToResponse).ToList(),
            actions,
            [
                "We keep a record of what you were notified about, so your Samaaj can show "
                + "you your notifications.",
                "Where we sent a message to your email address or mobile number, we keep "
                + "that address alongside the message, and whether it reached the "
                + "provider we sent it through.",
                "We keep an audit record of actions taken on the platform, including yours. "
                + "This is how a Samaaj can account for decisions made about its members, "
                + "and is kept even if you later ask for your account to be erased.",
            ],
            [
                "identity-tenant-service: your login, roles and consent history",
                "member-family-service: your profile, family and children",
            ]));
    }

    private static NotificationResponse ToResponse(Notification notification) => new(
        notification.Id,
        notification.Title,
        notification.Body,
        notification.Channel.ToString(),
        notification.Status.ToString(),
        notification.RecipientUserId is null,
        notification.CreatedAt,
        notification.ReadAt,
        notification.Destination);
}
