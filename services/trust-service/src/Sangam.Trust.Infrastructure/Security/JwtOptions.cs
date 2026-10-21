namespace Sangam.Trust.Infrastructure.Security;

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
    /// The mark the published local-development key carries (docker-compose and
    /// appsettings.Development.json). Anybody can read that key in this
    /// repository, so a token signed with it proves nothing.
    /// </summary>
    public const string LocalDevelopmentKeyMarker = "do-not-use-in-production";

    /// <summary>
    /// False for the published local key anywhere but Development - so a
    /// deployment that forgot to inject its own key refuses to start rather
    /// than accept tokens anybody could sign. Read from the process
    /// environment because options validation runs before a host is at hand.
    /// </summary>
    public static bool IsAllowedHere(string signingKey) =>
        !signingKey.Contains(LocalDevelopmentKeyMarker, StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Unused here. This service validates tokens and never issues one, so the
    /// lifetime is identity-tenant-service's decision; the property stays only
    /// because the three services share one Jwt configuration shape.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;
}
