using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sangam.Gateway.Tenancy;

/// <summary>
/// Resolves a slug through <see cref="ITenantCache"/>, falling back to
/// identity-tenant-service on a miss.
/// </summary>
public sealed class CachedTenantResolver(
    IHttpClientFactory httpClientFactory,
    ITenantCache cache,
    IOptions<GatewayOptions> options)
    : ITenantResolver
{
    public const string HttpClientName = "identity";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly GatewayOptions _options = options.Value;

    public async Task<ResolvedTenant?> ResolveAsync(string slug, CancellationToken cancellationToken = default)
    {
        if (await cache.GetAsync(slug) is { } cached)
        {
            return cached.Found ? cached.Tenant : null;
        }

        var tenant = await FetchAsync(slug, cancellationToken);

        // Negative results are cached too. Otherwise a mistyped or probed
        // subdomain becomes an unthrottled stream of lookups against identity.
        await cache.SetAsync(slug, tenant, TimeSpan.FromSeconds(_options.TenantCacheSeconds));

        return tenant;
    }

    private async Task<ResolvedTenant?> FetchAsync(string slug, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        var response = await client.GetAsync($"/v1/identity/tenants/{slug}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // Anything else means "we could not check", which must not be reported
        // as "no such Samaaj". The middleware turns the resulting exception
        // into a 502 rather than a 404.
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ResolvedTenant>(JsonOptions, cancellationToken);
    }
}
