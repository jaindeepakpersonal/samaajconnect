using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Security;

namespace Sangam.AuditNotification.Application.Notifications.Queries.GetMyNotifications;

/// <summary>
/// The caller's own notifications plus their Samaaj's broadcasts. Every
/// authenticated role may read their own, so the full role list is listed
/// explicitly - an unannotated request is denied.
/// </summary>
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
public sealed record GetMyNotificationsQuery(int Limit = 50)
    : IQuery<IReadOnlyList<NotificationResponse>>;
