using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Infrastructure.Security;

namespace Sangam.MasterData.Api.Extensions;

/// <summary>
/// Resolves the tenant for one request. See celebrity-voting-service's copy of
/// this same file for the full precedence reasoning: override header, then the
/// token's own claim, then the gateway's X-Tenant-Id header for an anonymous
/// request - which most requests to this service are, since only Gautra reads
/// and writes are tenant-scoped.
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string TenantOverrideHeader = "X-Tenant-Override-Id";

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        var httpContext = accessor.HttpContext;

        if (httpContext is null)
        {
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
