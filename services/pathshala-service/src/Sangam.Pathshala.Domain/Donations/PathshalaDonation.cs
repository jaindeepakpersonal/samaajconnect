using Sangam.Pathshala.Domain.Common;

namespace Sangam.Pathshala.Domain.Donations;

/// <summary>
/// One donation made to a Pathshala, as its own ledger records it - paid, or
/// announced and still to be paid.
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
/// <b>An announcement and a receipt are two steps</b> (2026-10-24). A member
/// often announces a gift and pays later, so a donation is recorded either
/// <see cref="DonationStatus.Paid"/> or <see cref="DonationStatus.Pledged"/>.
/// A pledge has no receipt number and no mode; <see cref="MarkPaid"/> gives it
/// both. A number on a slip therefore always means money received - the rule
/// trust-service's ledger keeps.
/// </para>
/// <para>
/// <b>Deleting takes an entry off the ledger; it does not erase it.</b> A receipt
/// number that was handed to somebody has to keep pointing at something, and
/// "what happened to receipt 42?" needs an answer - so a deleted entry keeps its
/// row (and its reason, required once a receipt exists), leaves the totals and
/// the ledger's default view, and its number is never issued again.
/// </para>
/// </remarks>
public sealed class PathshalaDonation : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid PathshalaId { get; private set; }

    public DonationStatus Status { get; private set; }

    /// <summary>
    /// Sequential per Pathshala, from 1, and issued only once the money is in -
    /// null on a pledge. Unique per Pathshala in the database, which is what
    /// holds when two are issued at once.
    /// </summary>
    public int? ReceiptNumber { get; private set; }

    /// <summary>The donor as written on the receipt - a member's name or anybody's.</summary>
    public string DonorName { get; private set; } = string.Empty;

    /// <summary>The donor's member id, when they are a member of this Samaaj.</summary>
    public Guid? DonorMemberId { get; private set; }

    /// <summary>Integer paise, like every amount on this platform.</summary>
    public long Amount { get; private set; }

    /// <summary>The day it was given or, for a pledge, announced.</summary>
    public DateOnly DonatedOn { get; private set; }

    /// <summary>How it was paid. Null on a pledge: nobody knows yet.</summary>
    public DonationMode? Mode { get; private set; }

    /// <summary>The day the money came in. Null on a pledge.</summary>
    public DateOnly? PaidOn { get; private set; }

    public string? Purpose { get; private set; }

    public Guid RecordedBy { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public DateTimeOffset? VoidedAt { get; private set; }
    public Guid? VoidedBy { get; private set; }
    public string? VoidReason { get; private set; }

    public bool IsVoid => VoidedAt is not null;

    private PathshalaDonation() { }   // EF Core

    /// <summary>Money received: a receipt number and a mode, now.</summary>
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
        ArgumentOutOfRangeException.ThrowIfLessThan(receiptNumber, 1);

        var donation = New(tenantId, pathshalaId, donorName, donorMemberId, amount, donatedOn, purpose, recordedBy, now);
        donation.Status = DonationStatus.Paid;
        donation.ReceiptNumber = receiptNumber;
        donation.Mode = mode;
        donation.PaidOn = donatedOn;

        donation.RaiseRecorded();

        return donation;
    }

    /// <summary>A gift announced and still to be paid: no receipt, no mode.</summary>
    public static PathshalaDonation Pledge(
        Guid tenantId,
        Guid pathshalaId,
        string donorName,
        Guid? donorMemberId,
        long amount,
        DateOnly announcedOn,
        string? purpose,
        Guid recordedBy,
        DateTimeOffset now)
    {
        var donation = New(tenantId, pathshalaId, donorName, donorMemberId, amount, announcedOn, purpose, recordedBy, now);
        donation.Status = DonationStatus.Pledged;

        donation.RaiseRecorded();

        return donation;
    }

    /// <summary>
    /// The pledged money came in: issues the receipt number the caller reserved.
    /// False when it is not an outstanding pledge - already paid, or deleted.
    /// </summary>
    public bool MarkPaid(int receiptNumber, DonationMode mode, DateOnly paidOn, Guid paidBy, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(receiptNumber, 1);

        if (Status != DonationStatus.Pledged || IsVoid)
        {
            return false;
        }

        Status = DonationStatus.Paid;
        ReceiptNumber = receiptNumber;
        Mode = mode;
        PaidOn = paidOn;

        Raise(new DonationPaidDomainEvent(Id, TenantId, PathshalaId, receiptNumber, Amount, paidBy, now));

        return true;
    }

    /// <summary>
    /// Corrects what was written down. A pledge stays a pledge, so its mode and
    /// paid date are ignored; a paid entry needs both. False on a deleted entry.
    /// </summary>
    public bool Correct(
        string donorName,
        long amount,
        DateOnly donatedOn,
        DonationMode? mode,
        DateOnly? paidOn,
        string? purpose,
        Guid updatedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(donorName);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);

        if (IsVoid)
        {
            return false;
        }

        if (Status == DonationStatus.Paid)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            PaidOn = paidOn ?? throw new ArgumentNullException(nameof(paidOn));
        }

        DonorName = donorName.Trim();
        Amount = amount;
        DonatedOn = donatedOn;
        Purpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;

        // Ids only: what changed is free text that belongs in this ledger.
        Raise(new DonationUpdatedDomainEvent(Id, TenantId, PathshalaId, updatedBy, now));

        return true;
    }

    /// <summary>
    /// Takes the entry off the ledger and out of the totals. False when it
    /// already is. The reason is optional on a pledge, which no receipt names.
    /// </summary>
    public bool Void(string? reason, Guid voidedBy, DateTimeOffset now)
    {
        if (ReceiptNumber is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        }

        if (IsVoid)
        {
            return false;
        }

        VoidedAt = now;
        VoidedBy = voidedBy;
        VoidReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        Raise(new DonationVoidedDomainEvent(Id, TenantId, PathshalaId, ReceiptNumber, voidedBy, now));

        return true;
    }

    private static PathshalaDonation New(
        Guid tenantId,
        Guid pathshalaId,
        string donorName,
        Guid? donorMemberId,
        long amount,
        DateOnly donatedOn,
        string? purpose,
        Guid recordedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(donorName);
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, 1);

        return new PathshalaDonation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PathshalaId = pathshalaId,
            DonorName = donorName.Trim(),
            DonorMemberId = donorMemberId,
            Amount = amount,
            DonatedOn = donatedOn,
            Purpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim(),
            RecordedBy = recordedBy,
            RecordedAt = now,
        };
    }

    // Ids and the amount only - the donor's name and the purpose are free text
    // that belongs in this ledger, where it can be corrected, and not in the
    // append-only audit log.
    private void RaiseRecorded() =>
        Raise(new DonationRecordedDomainEvent(
            Id, TenantId, PathshalaId, ReceiptNumber, Amount, Status == DonationStatus.Pledged, RecordedBy, RecordedAt));
}

public enum DonationStatus
{
    /// <summary>Announced, not yet paid.</summary>
    Pledged = 1,
    Paid = 2,
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
    int? ReceiptNumber,
    long Amount,
    bool Pledged,
    Guid RecordedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.donation.recorded.v1";
}

/// <summary>A pledge was paid, and given its receipt number.</summary>
public sealed record DonationPaidDomainEvent(
    Guid DonationId,
    Guid TenantId,
    Guid PathshalaId,
    int ReceiptNumber,
    long Amount,
    Guid PaidBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.donation.paid.v1";
}

/// <summary>An entry was corrected. What changed stays in the ledger; the event carries ids.</summary>
public sealed record DonationUpdatedDomainEvent(
    Guid DonationId,
    Guid TenantId,
    Guid PathshalaId,
    Guid UpdatedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.donation.updated.v1";
}

/// <summary>An entry was deleted from the ledger. The reason stays here; the event carries ids.</summary>
public sealed record DonationVoidedDomainEvent(
    Guid DonationId,
    Guid TenantId,
    Guid PathshalaId,
    int? ReceiptNumber,
    Guid VoidedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "pathshala.donation.voided.v1";
}
