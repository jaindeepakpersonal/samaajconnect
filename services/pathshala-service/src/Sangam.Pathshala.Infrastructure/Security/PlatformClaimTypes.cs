namespace Sangam.Pathshala.Infrastructure.Security;

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

    public const string Role = "role";

    public const string Permission = "permission";
}
