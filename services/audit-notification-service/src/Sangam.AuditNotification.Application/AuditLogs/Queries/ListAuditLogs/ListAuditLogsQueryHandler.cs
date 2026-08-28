using MediatR;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Common;

namespace Sangam.AuditNotification.Application.AuditLogs.Queries.ListAuditLogs;

public sealed class ListAuditLogsQueryHandler(IAuditLogQueries auditLogs)
    : IRequestHandler<ListAuditLogsQuery, Result<IReadOnlyList<AuditLogResponse>>>
{
    public async Task<Result<IReadOnlyList<AuditLogResponse>>> Handle(
        ListAuditLogsQuery query,
        CancellationToken cancellationToken)
    {
        var results = await auditLogs.ListAsync(
            query.Action, query.EntityName, query.Limit, cancellationToken);

        return Result.Success(results);
    }
}
