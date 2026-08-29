using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Infrastructure.Security;

namespace Sangam.Timeline.Api.Extensions;

/// <summary>
/// Resolves the tenant for one request. Never reads a tenant id from the
/// request body or query string - that would let any caller name their own
/// tenant (SECURITY-CHECKLIST.md).
/// </summary>
/// <remarks>
/// Precedence matters here:
/// <list type="number">
/// <item>a Super Admin override header, which the pipeline separately refuses
/// to anyone who is not a Super Admin;</item>
/// <item>the <c>tenant_id</c> claim on the validated token, because a signed
/// claim outranks an unsigned header;</item>
/// <item>the gateway's <c>X-Tenant-Id</c> header, which is all an anonymous
/// request has.</item>
/// </list>
/// A token claim that disagrees with the header is reported as a conflict
/// rather than silently resolved: that combination is what an attempt to point
/// one Samaaj's token at another Samaaj's data looks like.
/// </remarks>
public sealed class HttpTenantContext : ITenantContext
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string TenantOverrideHeader = "X-Tenant-Override-Id";

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        var httpContext = accessor.HttpContext;

        if (httpContext is null)
        {
            // Background work (the outbox dispatcher) has no request and no
            // tenant. Legitimate, so this stays null rather than throwing.
            return;
        }

        if (TryReadGuid(httpContext, TenantOverrideHeader, out var overrideTenantId))
        {
            TenantId = overrideTenantId;
            IsOverride = true;
            return;
        }

        var hasHeader = TryReadGuid(httpContext, TenantHeader, out var headerTenantId);

        var claim = httpContext.User.FindFirst(PlatformClaimTypes.TenantId)?.Value;

        if (httpContext.User.Identity?.IsAuthenticated == true && Guid.TryParse(claim, out var claimTenantId))
        {
            TenantId = claimTenantId;
            HasTenantConflict = hasHeader && headerTenantId != claimTenantId;
            return;
        }

        if (hasHeader)
        {
            TenantId = headerTenantId;
        }
    }

    public Guid? TenantId { get; }

    public bool IsOverride { get; }

    public bool HasTenantConflict { get; }

    public Guid RequireTenantId() => TenantId
        ?? throw new InvalidOperationException(
            "This operation is tenant-scoped but no tenant was resolved for the request.");

    private static bool TryReadGuid(HttpContext httpContext, string header, out Guid value)
    {
        value = Guid.Empty;

        return httpContext.Request.Headers.TryGetValue(header, out var raw)
            && Guid.TryParse(raw.ToString(), out value);
    }
}
