using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Sangam.Gateway.Tenancy;

/// <summary>
/// Turns the request host into an <c>X-Tenant-Id</c> header the downstream
/// services can trust, and rejects unknown or inactive Samaaj before any
/// service sees the request (ARCHITECTURE.md section 6).
/// </summary>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    HostSlugExtractor slugExtractor,
    IOptions<GatewayOptions> options,
    ILogger<TenantResolutionMiddleware> logger)
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string TenantOverrideHeader = "X-Tenant-Override-Id";
    public const string TenantSlugHeader = "X-Tenant-Slug";

    /// <summary>Set on HttpContext.Items so the module gate can read it without resolving twice.</summary>
    public const string TenantItemKey = "gateway.tenant";

    private readonly GatewayOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver)
    {
        // Stripped unconditionally and first. Downstream services treat these
        // headers as gateway-issued facts, so a client must never be able to
        // supply its own and pick a Samaaj.
        var clientOverride = context.Request.Headers[TenantOverrideHeader].ToString();

        context.Request.Headers.Remove(TenantHeader);
        context.Request.Headers.Remove(TenantOverrideHeader);
        context.Request.Headers.Remove(TenantSlugHeader);

        switch (await TryApplyOverrideAsync(context, clientOverride))
        {
            case OverrideOutcome.Applied:
                await next(context);
                return;

            case OverrideOutcome.Rejected:
                // TryApplyOverrideAsync has already written the response.
                return;
        }

        var slug = slugExtractor.Extract(context.Request.Host.Value);

        if (slug is null)
        {
            // An apex-host request carries no Samaaj. Registration and common
            // login both live here, so this is normal rather than an error.
            await next(context);
            return;
        }

        ResolvedTenant? tenant;

        try
        {
            tenant = await resolver.ResolveAsync(slug, context.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not resolve Samaaj {Slug}", slug);

            await WriteProblemAsync(
                context,
                StatusCodes.Status502BadGateway,
                "Tenant.ResolutionFailed",
                "The Samaaj directory is temporarily unavailable.");

            return;
        }

        if (tenant is null || !tenant.IsActive)
        {
            // 404 for both. An inactive Samaaj is not distinguishable from one
            // that never existed, so probing subdomains reveals nothing.
            logger.LogInformation("Rejecting request for unknown or inactive Samaaj {Slug}", slug);

            await WriteProblemAsync(
                context, StatusCodes.Status404NotFound, "Tenant.NotFound", "No Samaaj matches that address.");

            return;
        }

        context.Items[TenantItemKey] = tenant;
        context.Request.Headers[TenantHeader] = tenant.Id.ToString();
        context.Request.Headers[TenantSlugHeader] = tenant.Slug;

        await next(context);
    }

    /// <summary>What the override check decided about this request.</summary>
    private enum OverrideOutcome
    {
        /// <summary>No override header; carry on with normal slug resolution.</summary>
        None,

        /// <summary>Override accepted and forwarded downstream.</summary>
        Applied,

        /// <summary>Override refused; the response has already been written.</summary>
        Rejected,
    }

    /// <summary>Handles a Super Admin acting on another Samaaj.</summary>
    private async Task<OverrideOutcome> TryApplyOverrideAsync(HttpContext context, string clientOverride)
    {
        if (string.IsNullOrWhiteSpace(clientOverride))
        {
            return OverrideOutcome.None;
        }

        if (!slugExtractor.IsAdminHost(context.Request.Host.Value))
        {
            logger.LogWarning(
                "Refused tenant override from non-admin host {Host}", context.Request.Host.Value);

            await WriteProblemAsync(
                context, StatusCodes.Status403Forbidden, "Tenant.OverrideDenied",
                "Tenant override is only available from the admin console.");

            return OverrideOutcome.Rejected;
        }

        if (!Guid.TryParse(clientOverride, out var overrideTenantId))
        {
            await WriteProblemAsync(
                context, StatusCodes.Status400BadRequest, "Tenant.OverrideInvalid",
                "The tenant override header is not a valid id.");

            return OverrideOutcome.Rejected;
        }

        if (!IsSuperAdmin(context.User))
        {
            logger.LogWarning(
                "Refused tenant override for {TenantId} from a caller who is not a Super Admin",
                overrideTenantId);

            await WriteProblemAsync(
                context, StatusCodes.Status403Forbidden, "Tenant.OverrideDenied",
                "You are not allowed to act on another Samaaj.");

            return OverrideOutcome.Rejected;
        }

        // SECURITY-CHECKLIST.md: logged on every request that carries one, with
        // both the actor and the Samaaj being acted upon - not only at sign-in.
        logger.LogWarning(
            "Tenant override: actor {ActorUserId} acting on Samaaj {TenantId} for {Method} {Path}",
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"),
            overrideTenantId,
            context.Request.Method,
            context.Request.Path);

        context.Request.Headers[TenantOverrideHeader] = overrideTenantId.ToString();

        return OverrideOutcome.Applied;
    }

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && (user.IsInRole("SuperAdmin")
            || user.FindAll("role").Any(c => c.Value == "SuperAdmin"));

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
