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
            // A module route reached without a resolved Samaaj cannot be
            // checked, so it is refused rather than let through unchecked.
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

    private static Task NotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/problem+json";

        return context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            title = "NotFound",
            status = StatusCodes.Status404NotFound,
            detail = "No such endpoint.",
        });
    }
}
