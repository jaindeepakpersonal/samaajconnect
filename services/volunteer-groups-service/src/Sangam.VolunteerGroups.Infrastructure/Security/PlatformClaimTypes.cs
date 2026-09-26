namespace Sangam.VolunteerGroups.Infrastructure.Security;

/// <summary>
/// Claim names every service on the platform agrees on. Identical in each
/// service by design: one service mints these claims and the other nine read
/// them, so a divergence here is a silent authorization failure rather than a
/// compile error.
/// </summary>
public static class PlatformClaimTypes
{
    /// <summary>The Samaaj this token is scoped to. Guid.Empty for a platform account.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// An Area Admin's own locality restriction, set only on a token minted for
    /// the AreaAdmin role - null for every other account. A downstream service
    /// reads this the same way it reads TenantId: off the validated token, never
    /// from a header, since a caller could otherwise widen their own scope by
    /// setting one.
    /// </summary>
    public const string LocalityId = "locality_id";

    public const string Role = "role";

    public const string Permission = "permission";
}
