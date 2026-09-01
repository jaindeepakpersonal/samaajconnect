using FluentValidation;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Security;

namespace Sangam.AuditNotification.Application.Notifications.Commands.MarkNotificationRead;

/// <summary>
/// Records that the caller has read one notification.
/// </summary>
/// <remarks>
/// Any authenticated role, because the thing being changed is the caller's own
/// relationship to a message rather than the message. What stops it being a way
/// to touch somebody else's data is the pair of checks in the handler: the
/// notification has to be in the caller's Samaaj, and it has to be addressed to
/// them or to everybody.
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
public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand<MarkNotificationReadResult>;

/// <param name="AlreadyRead">
/// True when the caller had already read it. Success either way: pressing it
/// twice is not a mistake to report, and an error here would make a client
/// that retries a request look like a client with a bug.
/// </param>
public sealed record MarkNotificationReadResult(Guid NotificationId, DateTimeOffset ReadAt, bool AlreadyRead);

public sealed class MarkNotificationReadCommandValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadCommandValidator() =>
        RuleFor(x => x.NotificationId).NotEmpty();
}
