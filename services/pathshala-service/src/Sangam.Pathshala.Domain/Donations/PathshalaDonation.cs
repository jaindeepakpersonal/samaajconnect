using Sangam.Pathshala.Domain.Common;

namespace Sangam.Pathshala.Domain.Donations;

/// <summary>
/// One donation made to a Pathshala, as its own ledger records it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Record-keeping only</b> (confirmed with the user, 2026-09-26): nothing here
/// takes a payment. Somebody at the Pathshala received the money and writes it
/// down, and the ledger issues the receipt number.
/// </para>
/// <para>
/// <b>Its own root, not part of the Pathshala aggregate</b>, for the reason
/// enrolments are not: a ledger only grows, and recording one donation should
/// not load every other one.
/// </para>
/// <para>
/// <b>A donation is voided, never deleted.</b> A receipt number that was handed
/// to somebody has to keep pointing at something, and "what happened to receipt
/// 42?" needs an answer - so a mistake is voided with a reason and stays in the
/// ledger, out of the totals.
/// </para>
/// </remarks>
public sealed class PathshalaDonation : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PathshalaId { get; private set; }

    /// <summary>
    /// Sequential per Pathshala, from 1. Unique per Pathshala in the database,
    /// which is what holds when two are recorded at once.
    /// </summary>
    public int ReceiptNumber { get; private set; }

    /// <summary>The donor as written on the receipt - a member's name or anybody's.</summary>
    public string DonorName { get; private set; } = string.Empty;

    /// <summary>The donor's member id, when they are a member of this Samaaj.</summary>
    public Guid? DonorMemberId { get; private set; }

    /// <summary>Integer paise, like every amount on this platform.</summary>
    public long Amount { get; private set; }

    public DateOnly DonatedOn { get; private set; }
    public DonationMode Mode { get; private set; }
    public string? Purpose { get; private set; }

    public Guid RecordedBy { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedBy { get; private set; }
    public string? VoidReason { get; private set; }

    public bool IsVoid => VoidedAt is not null;

    private PathshalaDonation() { }   // EF Core

    public static PathshalaDonation Record(
        Guid tenantId,
        Guid pathshalaId,
        int receiptNumber,
        string donorName,
        Guid? donorMemberId,
        long amount,
        DateOnly donatedOn,
        DonationMode mode,
        string? purpose,
        Guid recordedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(donorName);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(receiptNumber, 1);

        var donation = new PathshalaDonation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PathshalaId = pathshalaId,
            ReceiptNumber = receiptNumber,
            DonorName = donorName.Trim(),
            DonorMemberId = donorMemberId,
            Amount = amount,
            DonatedOn = donatedOn,
            Mode = mode,
            Purpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim(),
            RecordedBy = recordedBy,
            RecordedAt = now,
        };

        // Ids and the amount only - the donor's name and the purpose are free
        // text that belongs in this ledger, where it can be corrected, and not
        // in the append-only audit log.
        donation.Raise(new DonationRecordedDomainEvent(
            donation.Id, tenantId, pathshalaId, receiptNumber, amount, recordedBy, now));

        return donation;
    }

    /// <summary>Takes a mistaken entry out of the totals. False when it already is.</summary>
    public bool Void(string reason, Guid voidedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (IsVoid)
        {
            return false;
        }

        VoidedAt = now;
        VoidedBy = voidedBy;
        VoidReason = reason.Trim();

        Raise(new DonationVoidedDomainEvent(Id, TenantId, PathshalaId, ReceiptNumber, voidedBy, now));

        return true;
    }
}

public enum DonationMode
{
    Cash = 1,
    Upi = 2,
    Cheque = 3,
    BankTransfer = 4,
    Other = 5,
}

public sealed record DonationRecordedDomainEvent(
    Guid DonationId,
    Guid TenantId,
    Guid PathshalaId,
    int ReceiptNumber,
    long Amount,
    Guid RecordedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.donation.recorded.v1";
}

/// <summary>A donation was voided. The reason stays in the ledger; the event carries ids.</summary>
public sealed record DonationVoidedDomainEvent(
    Guid DonationId,
    Guid TenantId,
    Guid PathshalaId,
    int ReceiptNumber,
    Guid VoidedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.donation.voided.v1";
}
