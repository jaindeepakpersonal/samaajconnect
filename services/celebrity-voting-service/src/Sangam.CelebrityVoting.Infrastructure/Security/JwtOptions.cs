namespace Sangam.CelebrityVoting.Infrastructure.Security;

/// <summary>
/// Lives in Infrastructure rather than Api because both halves of the story -
/// this service issuing tokens and every service validating them - must agree
/// on issuer, audience and key.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "samaajconnect";

    public string Audience { get; set; } = "samaajconnect";

    /// <summary>HS256 signing key. Must be at least 32 characters.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Unused here. This service validates tokens and never issues one, so the
    /// lifetime is identity-tenant-service's decision; the property stays only
    /// because the three services share one Jwt configuration shape.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;
}
