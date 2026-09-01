using Sangam.AuditNotification.Domain.Common;

namespace Sangam.AuditNotification.Domain.Notifications;

/// <summary>
/// A message for one member, or for a whole Samaaj when
/// <see cref="RecipientUserId"/> is null.
/// </summary>
/// <remarks>
/// In-app notifications are the row itself: writing it is delivering it. Every
/// other channel has to leave the platform, so the row is also the delivery
/// record - which attempt it is on, when it was last tried, and why it failed
/// if it did. The whole state machine lives here rather than in the dispatcher
/// so that "how many times will this be sent" has one answer in one file.
/// </remarks>
public sealed class Notification : AggregateRoot, ITenantScopedEntity
{
    /// <summary>
    /// Attempts before a message is abandoned. Low on purpose: the failures
    /// worth retrying are a provider being briefly down, and five spread over
    /// the poll interval covers that. Beyond it the address is usually simply
    /// wrong, and re-sending to a wrong address is not a retry - it is another
    /// message to whoever does own it.
    /// </summary>
    public const int MaxDeliveryAttempts = 5;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>Null means a broadcast to the whole Samaaj.</summary>
    public Guid? RecipientUserId { get; private set; }

    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public NotificationChannel Channel { get; private set; }
    public NotificationStatus Status { get; private set; }

    /// <summary>
    /// Where an outbound message is going - an email address or a mobile
    /// number. Null for in-app, which is addressed by
    /// <see cref="RecipientUserId"/> and needs nothing else.
    /// </summary>
    /// <remarks>
    /// This is personal data, and it is here rather than in a standing copy of
    /// every member's contact details because a notification is the one moment
    /// the address is actually needed. The row is deleted outright on erasure
    /// (<c>IErasureRepository.DeleteNotificationsForAsync</c>), so the address
    /// goes with it; a contact directory in this service would have been a
    /// second place to remember to erase from.
    /// </remarks>
    public string? Destination { get; private set; }

    public int DeliveryAttempts { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }

    /// <summary>Why the last attempt failed. Kept after a later success too - see MarkDelivered.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Identifies the dispatcher pass that claimed this row, so a claim made by
    /// one process is distinguishable from a claim made by another. See
    /// <c>NotificationRepository.ClaimPendingAsync</c> for why that matters.
    /// </summary>
    public Guid? DeliveryClaimId { get; private set; }

    /// <summary>
    /// The outbox row that caused this notification, so a redelivered event
    /// does not produce a second copy of the same message.
    /// </summary>
    public Guid SourceMessageId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private Notification() { }

    /// <summary>
    /// A message to every member of one Samaaj.
    /// </summary>
    /// <remarks>
    /// In-app only, and not because nobody has got round to the rest. This
    /// service learns a member's contact address from the event that mentions
    /// it - registration, today - and holds no directory of its own, so there
    /// is no set of addresses here to send a Samaaj-wide message to. Emailing a
    /// broadcast needs somewhere to read every member's address from, which is
    /// the same missing piece as the DPDP s.8(6) duty to reach every affected
    /// person; see docs/product/DPDP-COMPLIANCE.md.
    /// </remarks>
    public static Notification Broadcast(
        Guid tenantId,
        string title,
        string body,
        Guid sourceMessageId,
        Guid sentBy,
        DateTimeOffset createdAt)
    {
        var notification = Create(
            tenantId,
            recipientUserId: null,
            title,
            body,
            NotificationChannel.InApp,
            sourceMessageId,
            createdAt);

        // Raised here rather than in the handler (CLAUDE.md §4.5). The event is
        // what makes a broadcast auditable: this service consumes every topic,
        // so publishing to itself turns "somebody messaged the whole Samaaj"
        // into an audit row with an actor on it.
        notification.Raise(new BroadcastSentDomainEvent(
            notification.Id, tenantId, notification.Title, sentBy, createdAt));

        return notification;
    }

    public static Notification Create(
        Guid tenantId,
        Guid? recipientUserId,
        string title,
        string body,
        NotificationChannel channel,
        Guid sourceMessageId,
        DateTimeOffset createdAt,
        string? destination = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RecipientUserId = recipientUserId,
            Title = title.Trim(),
            Body = body.Trim(),
            Channel = channel,
            SourceMessageId = sourceMessageId,
            CreatedAt = createdAt,
        };

        if (channel == NotificationChannel.InApp)
        {
            // Readable the moment the row exists, and addressed by user id, so
            // a destination here would be an unused copy of somebody's contact
            // details. Dropped rather than stored.
            notification.Status = NotificationStatus.Sent;
            notification.DeliveredAt = createdAt;

            return notification;
        }

        notification.Destination = destination?.Trim();

        if (string.IsNullOrWhiteSpace(notification.Destination))
        {
            // Failed immediately rather than left Pending: a message with
            // nowhere to go will not become deliverable by being retried, and
            // Pending would have the dispatcher pick it up five times to
            // discover that. Failed with a reason is also visible, where a
            // Pending row that never moves looks like a stuck dispatcher.
            notification.Status = NotificationStatus.Failed;
            notification.FailureReason = "No destination address for this recipient.";

            return notification;
        }

        notification.Status = NotificationStatus.Pending;

        return notification;
    }

    // There is deliberately no ClaimForDelivery() here, and it is the one
    // transition this aggregate does not own.
    //
    // Claiming has to be atomic across processes: two dispatchers that both
    // read a Pending row and both write Sending have both sent the message, and
    // the person receives it twice. Read-then-write in application code cannot
    // prevent that; a single conditional UPDATE can. So the claim is one SQL
    // statement in NotificationRepository.ClaimPendingAsync, the same exception
    // ErasureRepository makes and for the same reason - the database is the only
    // thing in a position to do it correctly.
    //
    // The attempt is counted by that statement, at claim time rather than on
    // failure, which is what stops a message that reliably kills the sender from
    // being retried forever: the attempt is already spent when the process dies.
    //
    // Every transition after the claim is here, where it can be read and tested
    // as a state machine.

    public void MarkDelivered(DateTimeOffset at)
    {
        Status = NotificationStatus.Sent;
        DeliveredAt = at;
        DeliveryClaimId = null;

        // FailureReason is left as it was. A message that went out on the third
        // attempt is still a message that failed twice, and erasing why loses
        // the only trace of a provider having been down.
    }

    /// <summary>
    /// Records a failed attempt. <paramref name="permanent"/> abandons the
    /// message now rather than after the attempt limit - for a rejected address
    /// or an unconfigured channel, where every remaining attempt would fail the
    /// same way.
    /// </summary>
    public void RecordDeliveryFailure(string reason, bool permanent, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        FailureReason = reason.Length <= 500 ? reason : reason[..500];
        LastAttemptAt = at;
        DeliveryClaimId = null;

        Status = permanent || DeliveryAttempts >= MaxDeliveryAttempts
            ? NotificationStatus.Failed
            : NotificationStatus.Pending;
    }

    /// <summary>
    /// Returns a row abandoned in <see cref="NotificationStatus.Sending"/> to
    /// Pending so another pass can try it. Returns false when the row is not
    /// stalled, or has no attempts left to give.
    /// </summary>
    /// <remarks>
    /// Without this, a dispatcher killed between claiming and marking leaves
    /// the message in Sending forever - silently undelivered, and invisible
    /// because nothing is failing. The timeout is what makes the difference
    /// between "in flight" and "abandoned" decidable at all; it must be
    /// comfortably longer than a real send takes, or a slow provider gets asked
    /// to deliver the same message twice.
    /// </remarks>
    public bool ReleaseStalledClaim(DateTimeOffset now, TimeSpan stalledAfter)
    {
        if (Status != NotificationStatus.Sending
            || LastAttemptAt is not { } attemptedAt
            || now - attemptedAt < stalledAfter)
        {
            return false;
        }

        DeliveryClaimId = null;

        if (DeliveryAttempts >= MaxDeliveryAttempts)
        {
            Status = NotificationStatus.Failed;
            FailureReason = $"Abandoned mid-delivery {MaxDeliveryAttempts} times; no attempts left.";

            return true;
        }

        Status = NotificationStatus.Pending;
        FailureReason = "A previous attempt was abandoned before it reported an outcome.";

        return true;
    }

    /// <summary>
    /// Whether this notification is one <paramref name="userId"/> is entitled
    /// to read - addressed to them, or broadcast to their whole Samaaj.
    /// </summary>
    /// <remarks>
    /// Here rather than in the handler because it is the rule that decides
    /// whether marking something read is a member tidying their own list or a
    /// member reaching into somebody else's. The handler still checks the
    /// tenant separately: this answers "is it mine", not "is it in my Samaaj",
    /// and a notification id from another Samaaj with a null recipient would
    /// otherwise look like a broadcast addressed to everybody.
    /// </remarks>
    public bool IsAddressedTo(Guid userId) =>
        RecipientUserId is null || RecipientUserId == userId;

    // There is deliberately no MarkRead() and no ReadAt here. A broadcast is one
    // row shared by a whole Samaaj, so a read flag on it would be marked by the
    // first member to open it and read for everyone after. Read state lives in
    // NotificationRead, one row per person per message.
}
