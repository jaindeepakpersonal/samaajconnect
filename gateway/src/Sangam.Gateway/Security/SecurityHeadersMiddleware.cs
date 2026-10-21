namespace Sangam.Gateway.Security;

/// <summary>
/// Security headers on every response the gateway sends - its own, and every
/// one it proxies, API and member portal alike.
/// </summary>
/// <remarks>
/// <para>
/// Set as the response starts, so a 429 from the rate limiter or a 404 from
/// the module gate carries them as surely as a 200 from a service. A header a
/// proxied response already carries is left as it is: the member portal's own
/// server sets the page policy for its HTML, and nothing here should overrule
/// the thing that knows what the page loads.
/// </para>
/// <para>
/// <b>Two content policies.</b> JSON is data, never a document to run, so an
/// API response gets <c>default-src 'none'</c>: if one were ever rendered as a
/// page, nothing in it could load or execute. HTML that arrives without its own
/// policy gets the portal's (see <c>apps/member-portal/src/server.ts</c>).
/// </para>
/// <para>
/// <b><c>Server</c> and <c>X-Powered-By</c> are removed</b>, including the ones
/// the services and the portal's Node server would forward: which software and
/// version answers is a head start for anybody matching it against a list of
/// known flaws. <b>HSTS is opt-in</b> (<c>SecurityHeaders:Hsts</c>), because it
/// is only meaningful - and only safe to send - where TLS is actually
/// terminated in front of this; the local stack is plain HTTP.
/// </para>
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions options)
{
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    public const string PageContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; "
        + "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; "
        + "frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            SetIfAbsent(headers, "X-Content-Type-Options", "nosniff");
            SetIfAbsent(headers, "X-Frame-Options", "DENY");
            SetIfAbsent(headers, "Referrer-Policy", "strict-origin-when-cross-origin");
            SetIfAbsent(headers, "Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()");
            SetIfAbsent(headers, "Cross-Origin-Opener-Policy", "same-origin");

            var isPage = context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true;
            SetIfAbsent(headers, "Content-Security-Policy", isPage ? PageContentSecurityPolicy : ApiContentSecurityPolicy);

            if (options.Hsts)
            {
                SetIfAbsent(headers, "Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            }

            return Task.CompletedTask;
        });

        return next(context);
    }

    private static void SetIfAbsent(IHeaderDictionary headers, string name, string value)
    {
        if (!headers.ContainsKey(name))
        {
            headers[name] = value;
        }
    }
}

public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    /// <summary>Send Strict-Transport-Security. Only where TLS terminates in front of the gateway.</summary>
    public bool Hsts { get; set; }
}
