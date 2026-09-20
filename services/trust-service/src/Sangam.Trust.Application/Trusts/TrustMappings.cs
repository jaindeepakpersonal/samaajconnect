namespace Sangam.Trust.Application.Trusts;

// See ITrustRepository's own remarks: Trust is always qualified as
// `Domain.Trusts.Trust`, never bare, because "Trust" also names a namespace
// segment of this solution.

internal static class TrustMappings
{
    public static TrustResponse ToResponse(this Domain.Trusts.Trust trust) => new(
        trust.Id,
        trust.LocalityId,
        trust.Name,
        trust.Status.ToString(),
        trust.CreatedAt);
}
