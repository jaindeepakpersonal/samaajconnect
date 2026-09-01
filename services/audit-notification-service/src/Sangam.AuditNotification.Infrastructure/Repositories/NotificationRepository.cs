using Microsoft.EntityFrameworkCore;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Notifications.Queries.ListBroadcasts;
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

    public Task<IReadOnlyList<MemberNotification>> ListForRecipientAsync(
        Guid recipientUserId,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListWithReadStateAsync(
            dbContext.Notifications
                // The member's notification list, not a delivery log. An emailed
                // copy of a message the member has already been shown in-app
                // would appear here as a duplicate of it.
                .Where(n => n.Channel == NotificationChannel.InApp),
            recipientUserId,
            limit,
            cancellationToken);

    public Task<IReadOnlyList<MemberNotification>> ListEveryChannelForRecipientAsync(
        Guid recipientUserId,
        int limit,
        CancellationToken cancellationToken = default) =>
        ListWithReadStateAsync(dbContext.Notifications, recipientUserId, limit, cancellationToken);

    /// <summary>
    /// Left-joins each notification to this member's read row, so a broadcast
    /// comes back read for the people who have read it and unread for everyone
    /// else — from one query, rather than one per row.
    /// </summary>
    private async Task<IReadOnlyList<MemberNotification>> ListWithReadStateAsync(
        IQueryable<Notification> source,
        Guid recipientUserId,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await source
            .AsNoTracking()
            // Tenant-filtered, so "broadcast" means this Samaaj's broadcast and
            // never another Samaaj's.
            .Where(n => n.RecipientUserId == recipientUserId || n.RecipientUserId == null)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new
            {
                Notification = n,
                ReadAt = dbContext.NotificationReads
                    .Where(r => r.NotificationId == n.Id && r.UserId == recipientUserId)
                    .Select(r => (DateTimeOffset?)r.ReadAt)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new MemberNotification(row.Notification, row.ReadAt))];
    }

    public Task<Notification?> FindByIdAsync(
        Guid notificationId, CancellationToken cancellationToken = default) =>
        dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId, cancellationToken);

    public async Task<IReadOnlyList<BroadcastResponse>> ListBroadcastsAsync(
        int limit, CancellationToken cancellationToken = default) =>
        await dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == null)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new BroadcastResponse(
                n.Id,
                n.Title,
                n.Body,
                n.CreatedAt,
                dbContext.NotificationReads.Count(r => r.NotificationId == n.Id)))
            .ToListAsync(cancellationToken);

    public void Add(Notification notification) => dbContext.Notifications.Add(notification);

    /// <summary>
    /// Inserts the read row unless this member already has one.
    /// </summary>
    /// <remarks>
    /// <c>ON CONFLICT DO NOTHING</c> rather than a check followed by an insert.
    /// Two requests to open the same notification - two tabs, a double tap, a
    /// client retrying - would both pass a check and the unique index would turn
    /// the second into an unhandled exception. Here the second simply writes
    /// nothing and the caller is told it was already read, which is what
    /// happened.
    ///
    /// It runs inside the transaction TransactionBehavior opened, so it commits
    /// or rolls back with the rest of the request like any other write.
    /// </remarks>
    public async Task<bool> TryRecordReadAsync(
        NotificationRead read, CancellationToken cancellationToken = default)
    {
        var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO notification_reads (id, notification_id, user_id, tenant_id, read_at)
             VALUES ({read.Id}, {read.NotificationId}, {read.UserId}, {read.TenantId}, {read.ReadAt})
             ON CONFLICT (notification_id, user_id) DO NOTHING
             """,
            cancellationToken);

        return affected > 0;
    }

    /// <summary>
    /// Marks everything the member can currently see as read, in one statement.
    /// </summary>
    /// <remarks>
    /// The set is chosen in SQL rather than by reading a page of notifications
    /// and writing rows for those: a member with more notifications than the
    /// page holds would otherwise press "mark all as read" and still have
    /// unread ones, which is the one thing that button must not do.
    ///
    /// <c>ON CONFLICT DO NOTHING</c> leaves already-read rows untouched, so the
    /// timestamps stay honest - the moment each was first opened, not the moment
    /// somebody cleared the list.
    ///
    /// The tenant is passed in and filtered on explicitly. This is raw SQL, so
    /// the global query filter does not apply to it, and a statement that
    /// selected notifications without naming the Samaaj would insert read rows
    /// against every Samaaj's broadcasts at once.
    /// </remarks>
    public Task<int> MarkEverythingReadAsync(
        Guid userId,
        Guid tenantId,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        var inApp = NotificationChannel.InApp.ToString();

        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO notification_reads (id, notification_id, user_id, tenant_id, read_at)
             SELECT gen_random_uuid(), n.id, {userId}, {tenantId}, {readAt}
             FROM notifications n
             WHERE n.tenant_id = {tenantId}
               AND n.channel = {inApp}
               AND (n.recipient_user_id = {userId} OR n.recipient_user_id IS NULL)
             ON CONFLICT (notification_id, user_id) DO NOTHING
             """,
            cancellationToken);
    }

    public Task<NotificationRead?> FindReadAsync(
        Guid notificationId, Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.NotificationReads
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.NotificationId == notificationId && r.UserId == userId, cancellationToken);

    /// <summary>
    /// Claims a batch for delivery in one statement, then reads back what it got.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is one of two writes in this service that change rows without going
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
