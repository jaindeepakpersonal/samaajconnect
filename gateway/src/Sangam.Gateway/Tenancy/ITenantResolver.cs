namespace Sangam.Gateway.Tenancy;

public interface ITenantResolver
{
    /// <summary>
    /// Resolves a subdomain slug to a Samaaj, or null when no such Samaaj
    /// exists. A resolution failure against the identity service is thrown, not
    /// returned as null: "the Samaaj does not exist" and "we could not check"
    /// must not produce the same 404.
    /// </summary>
    Task<ResolvedTenant?> ResolveAsync(string slug, CancellationToken cancellationToken = default);
}
