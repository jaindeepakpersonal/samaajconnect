using Sangam.AuditNotification.Application.AuditLogs;

namespace Sangam.AuditNotification.Application.Abstractions;

/// <summary>
/// Read side of the audit log. Separate from IAuditLogRepository because the
/// two have opposite tenant rules: the repository writes from a consumer with
/// no tenant context and must bypass the filter, while everything here is
/// tenant-filtered and must never bypass it.
/// </summary>
public interface IAuditLogQueries
{
    Task<IReadOnlyList<AuditLogResponse>> ListAsync(
        string? action, string? entityName, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rows where this member is the actor, for their own data export. Not the
    /// rows that merely mention them: see the remarks on GetMyDataQuery.
    /// </summary>
    Task<IReadOnlyList<Privacy.Queries.GetMyData.MyActionResponse>> ListForActorAsync(
        Guid actorUserId, int limit, CancellationToken cancellationToken = default);
}
