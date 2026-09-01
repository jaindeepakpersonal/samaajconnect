using Microsoft.EntityFrameworkCore;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Domain.Notifications;
using Sangam.AuditNotification.Infrastructure.Persistence;

namespace Sangam.AuditNotification.Infrastructure.Repositories;

public sealed class NotificationRepository(AuditNotificationDbContext dbContext) : INotificationRepository
{
    public Task<bool> AlreadyRaisedAsync(
        Guid sourceMessageId,
        NotificationChannel channel,
        CancellationToken cancellationToken = default) =>
        dbContext.Notifications
            // Same reason as the audit repository: written from a consumer that
            // has no tenant context to filter by.
            .IgnoreQueryFilters()
            .AnyAsync(n => n.SourceMessageId == sourceMessageId && n.Channel == channel, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListForRecipientAsync(
        Guid recipientUserId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.Notifications
            .AsNoTracking()
            // Tenant-filtered, so "broadcast" means this Samaaj's broadcast and
            // never another Samaaj's.
            .Where(n => n.RecipientUserId == recipientUserId || n.RecipientUserId == null)
            // The member's notification list, not a delivery log. An emailed
            // copy of a message the member has already been shown in-app would
            // appear here as a duplicate of it.
            .Where(n => n.Channel == NotificationChannel.InApp)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListEveryChannelForRecipientAsync(
        Guid recipientUserId,
        int limit,
        CancellationToken cancellationToken = default) =>
        await dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == recipientUserId || n.RecipientUserId == null)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(Notification notification) => dbContext.Notifications.Add(notification);

    /// <summary>
    /// Claims a batch for delivery in one statement, then reads back what it got.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one write in this service that changes rows without going
    /// through the aggregate, and unlike the erasure exception it is not about
    /// bulk - it is about atomicity. Two dispatchers polling for Pending rows
    /// will both read the same row and both send it, and the member gets the
    /// message twice. Sending an event to Kafka twice is free, because consumers
    /// are idempotent; sending somebody two text messages is not, and there is
    /// no idempotency on the far side of a phone.
    /// </para>
    /// <para>
    /// What makes it safe is that it is <i>one statement</i>: the condition and
    /// the write are evaluated together, so Postgres re-checks
    /// <c>status = 'Pending'</c> against the committed row before a second
    /// updater may have it. Splitting it into a select and an update is what
    /// breaks - and it is what the concurrency test in
    /// <c>NotificationDeliveryTests</c> was checked against, by replacing this
    /// method with that version and confirming the test failed.
    /// </para>
    /// <para>
    /// <c>FOR UPDATE SKIP LOCKED</c> is throughput, not correctness, and the
    /// same experiment showed it: removing it left the test passing. Without it
    /// a second dispatcher blocks on the first one's row locks and then finds
    /// nothing to do, so it is a wasted wait rather than a wrong answer. It
    /// stays because a queue whose readers serialise on each other stops being a
    /// queue as soon as there are two of them.
    /// </para>
    /// <para>
    /// The statement is sent verbatim through <c>ExecuteSqlInterpolatedAsync</c>
    /// rather than composed by EF, because EF wraps composed raw SQL in a
    /// subquery and a locking clause does not survive that intact - the same
    /// trap boli-service hit with its bid lock.
    /// </para>
    /// <para>
    /// The read-back is by claim id rather than by "whatever is Sending",
    /// so a batch is exactly the rows this pass claimed and never a row another
    /// dispatcher is in the middle of sending.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<Notification>> ClaimPendingAsync(
        Guid claimId,
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return [];
        }

        var pending = NotificationStatus.Pending.ToString();
        var sending = NotificationStatus.Sending.ToString();
        var maxAttempts = Notification.MaxDeliveryAttempts;

        var claimed = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE notifications
             SET status = {sending},
                 delivery_claim_id = {claimId},
                 delivery_attempts = delivery_attempts + 1,
                 last_attempt_at = {now}
             WHERE id IN (
                 SELECT id
                 FROM notifications
                 WHERE status = {pending}
                   AND delivery_attempts < {maxAttempts}
                 ORDER BY created_at
                 LIMIT {batchSize}
                 FOR UPDATE SKIP LOCKED)
             """,
            cancellationToken);

        if (claimed == 0)
        {
            return [];
        }

        return await dbContext.Notifications
            // Tracked, not AsNoTracking: the dispatcher records the outcome on
            // these very instances and saves them.
            .IgnoreQueryFilters()
            .Where(n => n.DeliveryClaimId == claimId)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> ListStalledAsync(
        DateTimeOffset now,
        TimeSpan stalledAfter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var cutoff = now - stalledAfter;

        return await dbContext.Notifications
            .IgnoreQueryFilters()
            .Where(n => n.Status == NotificationStatus.Sending
                        && n.LastAttemptAt != null
                        && n.LastAttemptAt <= cutoff)
            .OrderBy(n => n.LastAttemptAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
