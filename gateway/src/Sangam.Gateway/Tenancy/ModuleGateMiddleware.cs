using Yarp.ReverseProxy.Model;

namespace Sangam.Gateway.Tenancy;

/// <summary>
/// Blocks routes belonging to a module this Samaaj has switched off.
/// </summary>
/// <remarks>
/// The module key lives in YARP route metadata (<c>Metadata.module</c>) rather
/// than in a table here, so adding a service means adding one route block and
/// nothing else — which is what `gateway/CLAUDE.md` promises.
///
/// A blocked route answers 404, not 403: a Samaaj that does not run a Pathshala
/// should be indistinguishable from a platform that has no Pathshala feature at
/// all (ARCHITECTURE.md section 6).
///
/// **That 404 is only for callers the gate could actually decide about.** A
/// request with no usable token has no Samaaj, so there is nothing to check,
/// and answering 404 there conflates "your Samaaj does not run this" with "you
/// have not said who you are". It also broke both portals: their interceptors
/// renew an expired access token on a 401 and retry, and a 404 sails past that
/// straight to the screen. Fifteen minutes after signing in, every
/// module-gated screen in the app said "No such endpoint." and stayed that way,
/// because nothing on those screens could ever produce the 401 that would have
/// renewed the token.
///
/// Answering 401 there conceals nothing extra: it is the same answer for every
/// gated route whether or not the Samaaj runs the module, precisely because no
/// Samaaj has been established. The concealment that matters — a signed-in
/// member being unable to tell a switched-off module from a feature that does
/// not exist — is untouched.
/// </remarks>
public sealed class ModuleGateMiddleware(RequestDelegate next, ILogger<ModuleGateMiddleware> logger)
{
    public const string ModuleMetadataKey = "module";

    public async Task InvokeAsync(HttpContext context)
    {
        var moduleKey = context.GetReverseProxyFeature()?.Route.Config.Metadata
            ?.GetValueOrDefault(ModuleMetadataKey);

        // Routes with no module key are platform infrastructure - identity,
        // audit, notifications - and are never gated.
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            await next(context);
            return;
        }

        if (context.Items[TenantResolutionMiddleware.TenantItemKey] is not ResolvedTenant tenant)
        {
            // No Samaaj was resolved, and why decides the answer.
            if (context.User.Identity?.IsAuthenticated != true)
            {
                // Anonymous, or holding a token that no longer validates. Ask
                // them to authenticate rather than reporting the route missing,
                // so an expired token can be renewed and the request retried.
                await UnauthorizedAsync(context);
                return;
            }

            // Authenticated with no Samaaj: a Super Admin, who belongs to the
            // platform rather than to a Samaaj, and who has not named one with
            // X-Tenant-Override-Id. Nothing to check the module against, so it
            // is refused rather than let through unchecked.
            logger.LogWarning(
                "Module route {Path} reached without a resolved Samaaj; refusing", context.Request.Path);

            await NotFoundAsync(context);
            return;
        }

        if (!tenant.HasModule(moduleKey))
        {
            logger.LogInformation(
                "Samaaj {Slug} does not run module {Module}; answering 404 for {Path}",
                tenant.Slug, moduleKey, context.Request.Path);

            await NotFoundAsync(context);
            return;
        }

        await next(context);
    }

    private static Task NotFoundAsync(HttpContext context) => WriteProblemAsync(
        context,
        StatusCodes.Status404NotFound,
        "NotFound",
        "No such endpoint.");

    private static Task UnauthorizedAsync(HttpContext context)
    {
        // The portals key their token renewal off the 401 alone, but a gateway
        // that refuses for want of credentials should say so properly.
        context.Response.Headers.WWWAuthenticate = "Bearer";

        return WriteProblemAsync(
            context,
            StatusCodes.Status401Unauthorized,
            "Auth.Required",
            "You must be signed in to use this.");
    }

    private static Task WriteProblemAsync(
        HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        return context.Response.WriteAsJsonAsync(new
        {
            type = $"https://tools.ietf.org/html/rfc9110#section-15.5.{statusCode - 399}",
            title,
            status = statusCode,
            detail,
        });
    }
}
