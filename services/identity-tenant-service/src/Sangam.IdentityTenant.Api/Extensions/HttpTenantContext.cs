using Sangam.IdentityTenant.Application.Abstractions;

namespace Sangam.IdentityTenant.Api.Extensions;

/// <summary>
/// Reads the tenant the gateway resolved from the subdomain. Never reads a
/// tenant id from the request body or query string - that would hand any
/// caller the ability to name their own tenant (SECURITY-CHECKLIST.md).
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
            // Background work (the outbox dispatcher) has no request and no
            // tenant. Legitimate, so this is null rather than an exception.
            return;
        }

        if (TryReadGuid(httpContext, TenantOverrideHeader, out var overrideTenantId))
        {
            TenantId = overrideTenantId;
            IsOverride = true;
            return;
        }

        if (TryReadGuid(httpContext, TenantHeader, out var tenantId))
        {
            TenantId = tenantId;
        }
    }

    public Guid? TenantId { get; }

    public bool IsOverride { get; }

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
