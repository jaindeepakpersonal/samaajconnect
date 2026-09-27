using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Donations;

/// <summary>A donation was recorded against a Trust. Starts Pledged.</summary>
/// <remarks>
/// No <c>Purpose</c>: it is free text an administrator typed, and
/// audit-notification-service stores payloads verbatim in an append-only
/// table. It stays in this service's own table, where it can be corrected.
/// </remarks>
public sealed record DonationRecordedDomainEvent(
    Guid DonationId,
    Guid TenantId,
    Guid TrustId,
    Guid? DonorMemberId,
    long Amount,
    string Mode,
    Guid RecordedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.donation.recorded.v1";
}

/// <summary>
/// A pledged donation was actually collected, and given a receipt number.
/// </summary>
public sealed record DonationCollectedDomainEvent(
    Guid DonationId,
    Guid TenantId,
    Guid TrustId,
    long Amount,
    int ReceiptNumber,
    Guid CollectedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "trust.donation.collected.v1";
}
