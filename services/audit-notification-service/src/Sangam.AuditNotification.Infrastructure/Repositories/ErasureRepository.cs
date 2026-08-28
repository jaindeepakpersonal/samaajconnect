using Microsoft.EntityFrameworkCore;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Infrastructure.Persistence;

namespace Sangam.AuditNotification.Infrastructure.Repositories;

public sealed class ErasureRepository(AuditNotificationDbContext dbContext) : IErasureRepository
{
    /// <summary>
    /// Written into every de-identified row so the state is obvious on sight,
    /// rather than looking like a row someone forgot to fill in.
    /// </summary>
    private const string Tombstone = "erased";

    public Task<int> DeleteNotificationsForAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            // Bypasses the tenant filter: a consumer has no request and so no
            // tenant, and the id it was given identifies exactly one person.
            .IgnoreQueryFilters()
            .Where(n => n.RecipientUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task<int> DeIdentifyAuditRowsForAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.ActorUserId == userId)
            // Only the fields that name a person. Action, entity, topic and
            // both timestamps are untouched, which is what keeps the row an
            // audit record rather than a hole where one was.
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(a => a.ActorUserId, (Guid?)null)
                    .SetProperty(a => a.ActorRole, Tombstone)
                    // The payload is the state of whatever changed and often
                    // repeats the person's details verbatim, so it cannot be
                    // kept. The row still says what happened and when.
                    .SetProperty(a => a.AfterState, "{}")
                    .SetProperty(a => a.BeforeState, (string?)null),
                cancellationToken);
}
