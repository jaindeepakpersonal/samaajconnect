using FluentValidation;
using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Security;

namespace Sangam.AuditNotification.Application.Notifications.Commands.BroadcastNotification;

/// <summary>
/// Announces something to every member of one Samaaj.
/// </summary>
/// <remarks>
/// <para>
/// One notification row with no recipient, not one row per member. A Samaaj of
/// two thousand people would otherwise mean two thousand rows saying the same
/// thing, and erasing one member would have to find their copy among them.
/// Read state is per member, in <c>NotificationRead</c>.
/// </para>
/// <para>
/// <b>This Samaaj only.</b> The admin wireframe's Audience dropdown also offers
/// "All Members" across every Samaaj and "Specific Role". The first is a write
/// that deliberately crosses tenants, which nothing else on this platform does
/// and which should not arrive as a side effect of a dropdown; the second needs
/// to know who holds which role, which lives in identity-tenant-service. Both
/// are absent rather than faked.
/// </para>
/// </remarks>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.NotificationsBroadcast)]
public sealed record BroadcastNotificationCommand(string Title, string Body)
    : ICommand<BroadcastNotificationResult>;

public sealed record BroadcastNotificationResult(Guid Id, DateTimeOffset SentAt);

public sealed class BroadcastNotificationCommandValidator : AbstractValidator<BroadcastNotificationCommand>
{
    public BroadcastNotificationCommandValidator()
    {
        // The same limits the column holds. A validator that is looser than the
        // schema turns a typo into a 500 at SaveChanges instead of a message
        // naming the field.
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Give the announcement a title.")
            .MaximumLength(200);

        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("An announcement with no message is not an announcement.")
            .MaximumLength(2000);
    }
}
