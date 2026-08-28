using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Sangam.Gateway.Tenancy;

/// <summary>
/// Per-caller rate limits on the endpoints an attacker guesses against.
/// </summary>
/// <remarks>
/// SECURITY-CHECKLIST.md asks for "rate limiting and brute-force lockout on
/// /login and any OTP endpoint". The lockout half already exists and is
/// per-account: five wrong passwords lock one account, five wrong activation
/// guesses kill one code.
///
/// That leaves the half a per-account counter cannot cover. Someone trying one
/// common password against ten thousand identifiers never trips an account
/// lockout - each account sees a single failure - and someone enumerating which
/// identifiers exist is not attacking an account at all. Both are shaped like
/// volume from one source, so the limit is per source.
///
/// It lives at the gateway rather than in identity-tenant-service because the
/// gateway is the only place that sees the caller before the request is
/// dispatched, and because a limit inside the service still costs a database
/// round trip per attempt.
///
/// <b>The partition key is the client IP, and the limits are deliberately
/// loose because of who shares one.</b> This platform serves communities in
/// India, where mobile carriers put very large numbers of subscribers behind
/// carrier-grade NAT - so a limit tuned to "a person signs in a few times a
/// minute" would lock out an entire carrier range, and a Samaaj hall running a
/// registration drive on one WiFi connection would trip it in the first
/// minute. The defaults are therefore set well above any plausible human
/// volume: they exist to make scripted attacks expensive, not to police
/// individuals. A password spray or an identifier enumeration wants thousands
/// of attempts a minute and is stopped; a determined attacker with many
/// addresses is not partitioned at all and is not the thing this catches.
///
/// The per-account lockout is what actually protects a specific person, and it
/// is unaffected by any of this.
///
/// When a real deployment puts a load balancer or CDN in front of this,
/// configure ForwardedHeaders - otherwise every request partitions into one
/// bucket keyed on the proxy, and the limit becomes a global cap that will take
/// the platform down under normal load.
/// </remarks>
public static class RateLimiting
{
    /// <summary>Sign-in and anything else that checks a secret against an identifier.</summary>
    public const string CredentialPolicy = "credential-attempts";

    /// <summary>Anything anonymous that creates something.</summary>
    public const string RegistrationPolicy = "registration";

    public static IServiceCollection AddGatewayRateLimiting(
        this IServiceCollection services,
        GatewayRateLimitOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                // A plain 429 with no body: telling a caller how long they have
                // to wait, or which limit they hit, is telling them how to pace
                // the next attempt.
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Sangam.Gateway.RateLimiting")
                    .LogWarning(
                        "Rate limit rejected {Method} {Path} from {Ip}",
                        context.HttpContext.Request.Method,
                        context.HttpContext.Request.Path,
                        Partition(context.HttpContext));

                await context.HttpContext.Response.WriteAsync(string.Empty, cancellationToken);
            };

            limiter.AddPolicy(CredentialPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    Partition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.CredentialAttemptsPerWindow,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),

                        // No queue. Holding a request until a slot frees is
                        // what a script wants and what a person experiences as
                        // the site hanging; a plain refusal is better for both.
                        QueueLimit = 0,
                    }));

            limiter.AddPolicy(RegistrationPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    Partition(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.RegistrationsPerWindow,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    private static string Partition(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

public sealed class GatewayRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// On by default. Set false in a test host that needs to make hundreds of
    /// credential attempts on purpose.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Sign-in and activation attempts per window, per source address. Set for
    /// carrier-grade NAT: far above what any number of real people behind one
    /// address would produce, far below what a credential-stuffing script does.
    /// </summary>
    public int CredentialAttemptsPerWindow { get; set; } = 300;

    /// <summary>
    /// Registrations per window, per source address. High enough that a Samaaj
    /// signing its members up together on one WiFi connection is unaffected.
    /// </summary>
    public int RegistrationsPerWindow { get; set; } = 100;
}
