namespace Sangam.Trust.Application.Donations;

public sealed record DonationResponse(
    Guid Id,
    Guid TrustId,
    Guid? DonorMemberId,
    string? DonorName,
    string? DonorPhone,
    long Amount,
    string Purpose,
    string Mode,
    string Status,
    int? ReceiptNumber,
    Guid RecordedBy,
    DateTimeOffset CreatedAt,
    Guid? CollectedBy,
    DateTimeOffset? CollectedAt);
