namespace Sangam.Gateway.Tenancy;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    /// <summary>
    /// Hostnames that carry no Samaaj. A request to one of these is
    /// tenant-less: registration and common login both happen here, before the
    /// member has been routed to a subdomain.
    /// </summary>
    public string[] ApexHosts { get; set; } = ["samaajconnect.com", "www.samaajconnect.com", "localhost"];

    /// <summary>
    /// The Super Admin console's host. Only requests arriving here may carry a
    /// tenant override, and even then only from a Super Admin.
    /// </summary>
    public string AdminHost { get; set; } = "admin.samaajconnect.com";

    /// <summary>
    /// How long a resolved Samaaj stays cached. Short enough that deactivating
    /// a Samaaj takes effect quickly, long enough that the identity service is
    /// not on the hot path of every request.
    /// </summary>
    public int TenantCacheSeconds { get; set; } = 60;

    /// <summary>Base address of identity-tenant-service, for slug resolution.</summary>
    public string IdentityServiceUrl { get; set; } = "http://identity-tenant-service:8080";
}
