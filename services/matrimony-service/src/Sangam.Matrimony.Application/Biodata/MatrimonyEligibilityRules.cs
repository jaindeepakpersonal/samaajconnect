using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;

namespace Sangam.Matrimony.Application.Biodata;

/// <summary>
/// "Matrimony is for members who are unmarried or divorced" (2026-10-24), the one
/// check every write and every read of somebody else's biodata makes.
/// </summary>
internal static class MatrimonyEligibilityRules
{
    public static readonly Error NotEligible = Error.Conflict(
        "Matrimony.NotEligible",
        "Matrimony biodata is for members who are unmarried or divorced. Set your marital status on My Profile.");

    public static async Task<bool> IsEligibleAsync(
        IEligibilityRepository eligibility, Guid memberId, CancellationToken cancellationToken) =>
        (await eligibility.GetAsync(memberId, cancellationToken))?.IsEligible == true;
}
