using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Collections;

// Referenced as `Domain.Collections.Collection`, never bare, in the
// Application layer - the same defensive convention `Domain.Trusts.Trust` and
// boli-service's `Domain.Auctions.Boli` already use, here because a bare
// `Collection` reads too easily as `System.Collections.ObjectModel.Collection<T>`
// the moment a file also needs a generic collection type.

/// <summary>
/// The payment ledger: what a Trust is owed, and whether it has come in.
/// Generalised over a <see cref="CollectionSourceType"/> rather than one
/// aggregate per source, because "is this due, and has it been collected" is
/// the identical question for a donation pledge and a Boli result - only
/// where the amount came from differs.
/// </summary>
public sealed class Collection : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TrustId { get; private set; }

    public CollectionSourceType SourceType { get; private set; }

    /// <summary>
    /// The originating record's own id - a <c>Donation.Id</c>, or the Boli
    /// result's id carried on <c>boli.result.published.v1</c>. Loose, like
    /// every other cross-service reference on this platform: no referential
    /// integrity, and for a <see cref="CollectionSourceType.BoliResult"/> row
    /// literally cross-service.
    /// </summary>
    public Guid SourceId { get; private set; }

    /// <summary>
    /// Who owes it - the Boli winner, or the donor when they are a member.
    /// Null for a donor with no account, who is named on the donation itself.
    /// </summary>
    public Guid? PayerMemberId { get; private set; }

    /// <summary>
    /// Who owes it, by name, when they are not a member - a donor with no
    /// account, named on the pledge. Null for a member, who is named from the
    /// directory instead.
    /// </summary>
    public string? PayerName { get; private set; }

    /// <summary>When the payer was last reminded. Null if never.</summary>
    public DateTimeOffset? LastRemindedAt { get; private set; }

    /// <summary>
    /// A reminder is a notification in somebody's inbox, so one a day at most:
    /// an officer pressing the button twice should not send it twice.
    /// </summary>
    public static readonly TimeSpan ReminderInterval = TimeSpan.FromDays(1);

    public long Amount { get; private set; }

    public CollectionStatus Status { get; private set; }

    public string? Mode { get; private set; }

    public Guid? CollectedBy { get; private set; }

    public DateTimeOffset? CollectedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private Collection() { }   // EF Core

    public static Collection Open(
        Guid tenantId,
        Guid trustId,
        CollectionSourceType sourceType,
        Guid sourceId,
        Guid? payerMemberId,
        long amount,
        DateTimeOffset now,
        string? payerName = null)
    {
        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TrustId = trustId,
            SourceType = sourceType,
            SourceId = sourceId,
            PayerMemberId = payerMemberId,
            PayerName = payerMemberId is null && !string.IsNullOrWhiteSpace(payerName) ? payerName.Trim() : null,
            Amount = amount,
            Status = CollectionStatus.Due,
            CreatedAt = now,
        };

        collection.Raise(new CollectionDueDomainEvent(
            collection.Id, tenantId, trustId, sourceType.ToString(), sourceId, payerMemberId, amount, now));

        return collection;
    }

    /// <summary>False when this row is not Due - settling twice is not a second fact.</summary>
    public bool Settle(string mode, Guid collectedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);

        if (Status != CollectionStatus.Due)
        {
            return false;
        }

        Status = CollectionStatus.Collected;
        Mode = mode.Trim();
        CollectedBy = collectedBy;
        CollectedAt = now;

        Raise(new CollectionSettledDomainEvent(Id, TenantId, TrustId, Amount, Mode, collectedBy, now));

        return true;
    }

    /// <summary>
    /// Reminds the payer that this is still due - a notification to a member,
    /// so only a member can be reminded; a donor with no account is phoned.
    /// </summary>
    public ReminderOutcome Remind(Guid remindedBy, DateTimeOffset now)
    {
        if (Status != CollectionStatus.Due)
        {
            return ReminderOutcome.NotDue;
        }

        if (PayerMemberId is not { } payer)
        {
            return ReminderOutcome.NotAMember;
        }

        if (LastRemindedAt is { } last && now - last < ReminderInterval)
        {
            return ReminderOutcome.TooSoon;
        }

        LastRemindedAt = now;

        Raise(new CollectionReminderSentDomainEvent(
            Id, TenantId, TrustId, payer, SourceType.ToString(), remindedBy, now));

        return ReminderOutcome.Sent;
    }
}

public enum ReminderOutcome
{
    Sent = 1,
    NotDue = 2,
    NotAMember = 3,
    TooSoon = 4,
}

public enum CollectionSourceType
{
    Donation = 1,
    BoliResult = 2,
}

public enum CollectionStatus
{
    Due = 1,
    Collected = 2,
}
