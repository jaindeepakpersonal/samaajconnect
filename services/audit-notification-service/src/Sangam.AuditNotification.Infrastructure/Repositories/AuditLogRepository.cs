using Microsoft.EntityFrameworkCore;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.AuditLogs;
using Sangam.AuditNotification.Application.Privacy.Queries.GetMyData;
using Sangam.AuditNotification.Domain.AuditLogs;
using Sangam.AuditNotification.Infrastructure.Persistence;

namespace Sangam.AuditNotification.Infrastructure.Repositories;

public sealed class AuditLogRepository(AuditNotificationDbContext dbContext) : IAuditLogRepository
{
    public Task<bool> AlreadyRecordedAsync(Guid sourceMessageId, CancellationToken cancellationToken = default) =>
        dbContext.AuditLogs
            // The consumer has no request and therefore no tenant, so the
            // filter would compare against Guid.Empty and match nothing -
            // turning every redelivery into a duplicate row.
            .IgnoreQueryFilters()
            .AnyAsync(a => a.SourceMessageId == sourceMessageId, cancellationToken);

    public void Add(AuditLog auditLog) => dbContext.AuditLogs.Add(auditLog);
}

/// <summary>
/// Read side. Never bypasses the tenant filter: everything here is reachable
/// from an HTTP request, so one Samaaj must not be able to read another's trail.
/// </summary>
public sealed class AuditLogQueries(AuditNotificationDbContext dbContext) : IAuditLogQueries
{
    public async Task<IReadOnlyList<AuditLogResponse>> ListAsync(
        string? action,
        string? entityName,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(entityName))
        {
            query = query.Where(a => a.EntityName == entityName);
        }

        return await query
            .OrderByDescending(a => a.OccurredAt)
            .Take(limit)
            .Select(a => new AuditLogResponse(
                a.Id,
                a.TenantId,
                a.Action,
                a.EntityName!,
                a.EntityId,
                a.ActorUserId,
                a.Topic,
                a.EventType,
                a.OccurredAt,
                a.RecordedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MyActionResponse>> ListForActorAsync(
        Guid actorUserId,
        int limit,
        CancellationToken cancellationToken = default) =>
        // Tenant-filtered, and the payload is deliberately not selected: it is
        // the state of whatever changed, which may be someone else's data.
        await dbContext.AuditLogs
            .AsNoTracking()
            .Where(a => a.ActorUserId == actorUserId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(limit)
            .Select(a => new MyActionResponse(a.Action, a.EntityName!, a.EntityId, a.OccurredAt))
            .ToListAsync(cancellationToken);
}
