using Microsoft.Extensions.Options;

namespace Sangam.Gateway.Tenancy;

/// <summary>
/// Pulls the Samaaj slug out of the request host.
/// </summary>
public sealed class HostSlugExtractor(IOptions<GatewayOptions> options)
{
    private readonly GatewayOptions _options = options.Value;

    /// <summary>
    /// Returns the slug, or null when the host carries no Samaaj - an apex
    /// host, the admin console, or a bare IP address.
    /// </summary>
    public string? Extract(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        // Host may arrive with a port attached (localhost:8080).
        var hostname = host.Split(':')[0].Trim().TrimEnd('.').ToLowerInvariant();

        if (hostname.Length == 0
            || _options.ApexHosts.Contains(hostname, StringComparer.OrdinalIgnoreCase)
            || string.Equals(hostname, _options.AdminHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // An IP address is never a Samaaj subdomain. Treating one as a slug
        // would send the identity service a lookup for "127" on every
        // health check from inside the cluster.
        if (System.Net.IPAddress.TryParse(hostname, out _))
        {
            return null;
        }

        var labels = hostname.Split('.', StringSplitOptions.RemoveEmptyEntries);

        // "mahavir-samaj.samaajconnect.com" -> "mahavir-samaj".
        // A single label such as "identity-tenant-service" is an internal
        // hostname, not a Samaaj, so it resolves to nothing.
        return labels.Length < 2 ? null : labels[0];
    }

    public bool IsAdminHost(string? host) =>
        host is not null
        && string.Equals(
            host.Split(':')[0].Trim().TrimEnd('.'),
            _options.AdminHost,
            StringComparison.OrdinalIgnoreCase);
}
