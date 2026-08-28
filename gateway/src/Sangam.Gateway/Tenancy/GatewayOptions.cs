namespace Sangam.Gateway.Tenancy;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>
    /// How long a resolved Samaaj stays cached. Short enough that deactivating
    /// a Samaaj, or switching a module off, takes effect quickly; long enough
    /// that identity-tenant-service is not on the hot path of every request.
    /// </summary>
    public int TenantCacheSeconds { get; set; } = 60;

    /// <summary>Base address of identity-tenant-service, for tenant lookups.</summary>
    public string IdentityServiceUrl { get; set; } = "http://identity-tenant-service:8080";
}
