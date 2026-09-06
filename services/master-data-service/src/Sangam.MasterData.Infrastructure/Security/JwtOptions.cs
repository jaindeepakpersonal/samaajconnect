namespace Sangam.MasterData.Infrastructure.Security;

/// <summary>
/// Lives in Infrastructure rather than Api for the same reason every other
/// service keeps it there - see celebrity-voting-service's copy.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "samaajconnect";

    public string Audience { get; set; } = "samaajconnect";

    /// <summary>HS256 signing key. Must be at least 32 characters.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Unused here - this service validates tokens and never issues one.</summary>
    public int AccessTokenMinutes { get; set; } = 15;
}
