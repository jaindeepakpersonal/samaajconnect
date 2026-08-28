using System.Security.Claims;

namespace Sangam.Gateway.Tenancy;

/// <summary>
/// Decides which Samaaj a request belongs to and tells the services behind it,
/// via a header they can trust.
/// </summary>
/// <remarks>
/// The platform runs on a single domain, so there is no subdomain to read: a
/// member signs in once and the token they get names their Samaaj. This
/// middleware turns that signed claim into an <c>X-Tenant-Id</c> header, having
/// first confirmed the Samaaj is still active — a token outlives a
/// deactivation, and the gateway is where that gets caught before any service
/// sees the request (root CLAUDE.md §6).
/// </remarks>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    ILogger<TenantResolutionMiddleware> logger)
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string TenantOverrideHeader = "X-Tenant-Override-Id";
    public const string TenantSlugHeader = "X-Tenant-Slug";

    /// <summary>Claim identity-tenant-service puts the Samaaj id in.</summary>
    public const string TenantClaimType = "tenant_id";

    public const string SuperAdminRole = "SuperAdmin";

    /// <summary>Set on HttpContext.Items so the module gate can read it without resolving twice.</summary>
    public const string TenantItemKey = "gateway.tenant";

    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver)
    {
        // Stripped unconditionally and first. Services treat these headers as
        // gateway-issued facts, so a client must never be able to supply its
        // own and pick a Samaaj.
        var requestedOverride = context.Request.Headers[TenantOverrideHeader].ToString();

        context.Request.Headers.Remove(TenantHeader);
        context.Request.Headers.Remove(TenantOverrideHeader);
        context.Request.Headers.Remove(TenantSlugHeader);

        var tenantId = ResolveTenantId(context, requestedOverride, out var isOverride);

        if (tenantId is null)
        {
            if (!string.IsNullOrWhiteSpace(requestedOverride))
            {
                // An override was asked for and refused; ResolveTenantId has
                // already logged why.
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Tenant.OverrideDenied",
                    "You are not allowed to act on another Samaaj.");

                return;
            }

            // Anonymous, or a token with no tenant claim - a Super Admin, who
            // belongs to the platform rather than to a Samaaj. Both are normal:
            // login, registration and the Samaaj directory all live here.
            await next(context);
            return;
        }

        ResolvedTenant? tenant;

        try
        {
            tenant = await resolver.ResolveAsync(tenantId.Value, context.RequestAborted);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not resolve Samaaj {TenantId}", tenantId);

            await WriteProblemAsync(
                context,
                StatusCodes.Status502BadGateway,
                "Tenant.ResolutionFailed",
                "The Samaaj directory is temporarily unavailable.");

            return;
        }

        if (tenant is null || !tenant.IsActive)
        {
            // 403 rather than 404: the caller holds a valid token, so this is
            // "your Samaaj is not available", not "no such address".
            logger.LogInformation(
                "Refusing request for unknown or inactive Samaaj {TenantId}", tenantId);

            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Tenant.Unavailable",
                "Your Samaaj is not currently active. Please contact your Samaaj administrator.");

            return;
        }

        context.Items[TenantItemKey] = tenant;
        context.Request.Headers[TenantSlugHeader] = tenant.Slug;

        // Downstream services treat an override exactly like a normal tenant
        // header; the distinct name exists so they can log that it happened,
        // not so they can behave differently (root CLAUDE.md §6).
        context.Request.Headers[isOverride ? TenantOverrideHeader : TenantHeader] =
            tenant.Id.ToString();

        await next(context);
    }

    /// <summary>
    /// Returns the Samaaj this request should run against, or null when there
    /// is none — which covers both an anonymous caller and a refused override.
    /// </summary>
    private Guid? ResolveTenantId(HttpContext context, string requestedOverride, out bool isOverride)
    {
        isOverride = false;

        if (!string.IsNullOrWhiteSpace(requestedOverride))
        {
            // With one domain there is no admin hostname to gate on, so the
            // role on the validated token is the whole gate.
            if (!IsSuperAdmin(context.User))
            {
                logger.LogWarning(
                    "Refused tenant override from a caller who is not a Super Admin: {Path}",
                    context.Request.Path);

                return null;
            }

            if (!Guid.TryParse(requestedOverride, out var overrideTenantId))
            {
                logger.LogWarning("Refused malformed tenant override header");

                return null;
            }

            // SECURITY-CHECKLIST.md: logged on every request that carries one,
            // with both the actor and the Samaaj acted upon. On a single domain
            // this log is the only record of who did what to whose Samaaj.
            logger.LogWarning(
                "Tenant override: actor {ActorUserId} acting on Samaaj {TenantId} for {Method} {Path}",
                ActorOf(context.User),
                overrideTenantId,
                context.Request.Method,
                context.Request.Path);

            isOverride = true;

            return overrideTenantId;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return Guid.TryParse(context.User.FindFirstValue(TenantClaimType), out var claimTenantId)
            && claimTenantId != Guid.Empty
                ? claimTenantId
                : null;
    }

    private static string? ActorOf(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
        && (user.IsInRole(SuperAdminRole)
            || user.FindAll("role").Any(claim => claim.Value == SuperAdminRole));

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
