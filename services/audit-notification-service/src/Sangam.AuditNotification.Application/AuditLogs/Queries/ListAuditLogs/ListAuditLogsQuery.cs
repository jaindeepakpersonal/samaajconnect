using Sangam.AuditNotification.Application.Common;
using Sangam.AuditNotification.Application.Security;

namespace Sangam.AuditNotification.Application.AuditLogs.Queries.ListAuditLogs;

/// <summary>
/// Reads the Samaaj's audit trail, newest first. Tenant-scoped by the query
/// filter, so a Samaaj Admin sees only their own Samaaj even though the table
/// holds every tenant's rows.
/// </summary>
[RequiresRoles(Roles.SuperAdmin, Roles.SamaajAdmin)]
[RequiresPermission(PermissionKeys.AuditRead)]
public sealed record ListAuditLogsQuery(string? Action, string? EntityName, int Limit = 50)
    : IQuery<IReadOnlyList<AuditLogResponse>>;
