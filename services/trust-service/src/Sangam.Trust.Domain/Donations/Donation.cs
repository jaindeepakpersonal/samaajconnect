using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Donations;

/// <summary>
/// One donation pledged to a Trust, and whether it has actually been
/// collected.
/// </summary>
/// <remarks>
/// Record-then-collect is a deliberate two-step split, the same discipline
/// <c>boli-service</c>'s own record-then-publish result carries: a pledge is
/// somebody's word, and a mis-entered amount or a pledge that never comes in
/// should be catchable before it is treated as money in hand. Money is
/// <c>long</c> paise throughout, never a floating-point type - this repo's
/// own hard rule, stated first in <c>boli-service/CLAUDE.md</c>.
/// </remarks>
public sealed class Donation : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TrustId { get; private set; }

    /// <summary>The donor's own member id, when the donor is a portal member.</summary>
    public Guid? DonorMemberId { get; private set; }

    /// <summary>Used when <see cref="DonorMemberId"/> is null - a donor need not hold an account.</summary>
    public string? DonorName { get; private set; }

    public string? DonorPhone { get; private set; }

    public long Amount { get; private set; }

    public string Purpose { get; private set; } = null!;

    public DonationMode Mode { get; private set; }

    public DonationStatus Status { get; private set; }

    /// <summary>Assigned only on collection - see <see cref="Collect"/>.</summary>
    public int? ReceiptNumber { get; private set; }

    public Guid RecordedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CollectedBy { get; private set; }

    public DateTimeOffset? CollectedAt { get; private set; }

    private Donation() { }   // EF Core

    public static Donation Record(
        Guid tenantId,
        Guid trustId,
        Guid? donorMemberId,
        string? donorName,
        string? donorPhone,
        long amount,
        string purpose,
        DonationMode mode,
        Guid recordedBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var donation = new Donation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TrustId = trustId,
            DonorMemberId = donorMemberId,
            DonorName = string.IsNullOrWhiteSpace(donorName) ? null : donorName.Trim(),
            DonorPhone = string.IsNullOrWhiteSpace(donorPhone) ? null : donorPhone.Trim(),
            Amount = amount,
            Purpose = purpose.Trim(),
            Mode = mode,
            Status = DonationStatus.Pledged,
            RecordedBy = recordedBy,
            CreatedAt = now,
        };

        donation.Raise(new DonationRecordedDomainEvent(
            donation.Id, tenantId, trustId, donorMemberId, amount, mode.ToString(), recordedBy, now));

        return donation;
    }

    /// <summary>
    /// Settles a pledge: assigns the receipt number the caller has already
    /// reserved (see <c>ITrustRepository.LockForReceiptNumberingAsync</c> for
    /// how that number is made safe under concurrency) and marks it Collected.
    /// False when this donation is not Pledged - already collected is not a
    /// second fact.
    /// </summary>
    public bool Collect(int receiptNumber, Guid collectedBy, DateTimeOffset now)
    {
        if (Status != DonationStatus.Pledged)
        {
            return false;
        }

        Status = DonationStatus.Collected;
        ReceiptNumber = receiptNumber;
        CollectedBy = collectedBy;
        CollectedAt = now;

        Raise(new DonationCollectedDomainEvent(Id, TenantId, TrustId, Amount, receiptNumber, collectedBy, now));

        return true;
    }
}

public enum DonationMode
{
    Cash = 1,
    Cheque = 2,
    Upi = 3,
    BankTransfer = 4,
}

public enum DonationStatus
{
    Pledged = 1,
    Collected = 2,
}
