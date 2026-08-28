using Microsoft.EntityFrameworkCore;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Domain.Notifications;
using Sangam.AuditNotification.Infrastructure.Persistence;

namespace Sangam.AuditNotification.Infrastructure.Repositories;

public sealed class NotificationRepository(AuditNotificationDbContext dbContext) : INotificationRepository
{
    public Task<bool> AlreadyRaisedAsync(Guid sourceMessageId, CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            // Same reason as the audit repository: written from a consumer that
            // has no tenant context to filter by.
            .IgnoreQueryFilters()
            .AnyAsync(n => n.SourceMessageId == sourceMessageId, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListForRecipientAsync(
        Guid recipientUserId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.Notifications
            .AsNoTracking()
            // Tenant-filtered, so "broadcast" means this Samaaj's broadcast and
            // never another Samaaj's.
            .Where(n => n.RecipientUserId == recipientUserId || n.RecipientUserId == null)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(Notification notification) => dbContext.Notifications.Add(notification);
}
