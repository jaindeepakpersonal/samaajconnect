using Sangam.Trust.Domain.Donations;

namespace Sangam.Trust.Application.Donations;

internal static class DonationMappings
{
    public static DonationResponse ToResponse(this Donation donation) => new(
        donation.Id,
        donation.TrustId,
        donation.DonorMemberId,
        donation.DonorName,
        donation.DonorPhone,
        donation.Amount,
        donation.Purpose,
        donation.Mode.ToString(),
        donation.Status.ToString(),
        donation.ReceiptNumber,
        donation.RecordedBy,
        donation.CreatedAt,
        donation.CollectedBy,
        donation.CollectedAt);
}
