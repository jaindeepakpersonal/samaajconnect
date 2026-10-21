namespace Sangam.IdentityTenant.Infrastructure.Security;

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
    /// How long an access token is good for.
    /// </summary>
    /// <remarks>
    /// Fifteen minutes, not the hour it used to be. An access token is
    /// stateless - every service validates it without calling back here - so
    /// nothing can withdraw one, and its lifetime is therefore the exact window
    /// in which a revoked role, a suspended account or a stolen token still
    /// works. An hour of that was too long once refresh tokens made a shorter
    /// one practical: the member is not signed out at fifteen minutes, their
    /// client just exchanges a refresh token, and that exchange re-checks the
    /// account, its Samaaj and its roles.
    ///
    /// Shorter would keep costing a round trip for less and less; the remaining
    /// window is documented in SECURITY-CHECKLIST.md rather than chased to zero,
    /// because closing it entirely means a lookup on every request in every
    /// service.
    /// </remarks>
    public int AccessTokenMinutes { get; set; } = 15;
}
