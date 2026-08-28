using System.Text.Json;
using StackExchange.Redis;

namespace Sangam.Gateway.Tenancy;

/// <summary>
/// A cached tenant lookup. <paramref name="Found"/> distinguishes "we cached the
/// fact that this Samaaj does not exist" from "we have nothing cached", which a
/// plain nullable tenant could not express.
/// </summary>
public sealed record CachedTenantLookup(bool Found, ResolvedTenant? Tenant);

public interface ITenantCache
{
    Task<CachedTenantLookup?> GetAsync(string tenantId);

    Task SetAsync(string tenantId, ResolvedTenant? tenant, TimeSpan ttl);
}

/// <summary>
/// Used when Redis is not configured or could not be reached at startup.
/// </summary>
/// <remarks>
/// A null object rather than a nullable dependency: the cache is an
/// optimisation, and the gateway must keep resolving tenants without it. Making
/// "no cache" a working implementation means no call site has to remember that.
/// </remarks>
public sealed class NullTenantCache : ITenantCache
{
    public Task<CachedTenantLookup?> GetAsync(string tenantId) => Task.FromResult<CachedTenantLookup?>(null);

    public Task SetAsync(string tenantId, ResolvedTenant? tenant, TimeSpan ttl) => Task.CompletedTask;
}

public sealed class RedisTenantCache(IConnectionMultiplexer redis, ILogger<RedisTenantCache> logger)
    : ITenantCache
{
    /// <summary>
    /// Stored in place of a tenant to remember "this Samaaj does not exist".
    /// Not valid JSON, so it can never be mistaken for a cached Samaaj.
    /// </summary>
    private const string NegativeCacheMarker = "-";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CachedTenantLookup?> GetAsync(string tenantId)
    {
        if (!redis.IsConnected)
        {
            return null;
        }

        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(tenantId));

            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return value == NegativeCacheMarker
                ? new CachedTenantLookup(false, null)
                : new CachedTenantLookup(true, JsonSerializer.Deserialize<ResolvedTenant>(value!, JsonOptions));
        }
        catch (Exception exception)
        {
            // A cache failure degrades to a cache miss, never to a failed
            // request: Redis must not become a second thing that can take the
            // whole platform down.
            logger.LogWarning(exception, "Redis lookup failed for {TenantId}; treating as a miss", tenantId);

            return null;
        }
    }

    public async Task SetAsync(string tenantId, ResolvedTenant? tenant, TimeSpan ttl)
    {
        if (!redis.IsConnected)
        {
            return;
        }

        try
        {
            var payload = tenant is null
                ? NegativeCacheMarker
                : JsonSerializer.Serialize(tenant, JsonOptions);

            await redis.GetDatabase().StringSetAsync(Key(tenantId), payload, ttl);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to cache Samaaj {TenantId}", tenantId);
        }
    }

    private static string Key(string tenantId) => $"gateway:tenant:{tenantId}";
}
