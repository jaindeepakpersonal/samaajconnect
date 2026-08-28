namespace Sangam.Gateway.Tenancy;

public interface ITenantResolver
{
    /// <summary>
    /// Looks up the Samaaj named by a token's <c>tenant_id</c> claim, or null
    /// when no such Samaaj exists. A failure to reach identity-tenant-service
    /// is thrown, not returned as null: "the Samaaj is gone" and "we could not
    /// check" must not produce the same answer.
    /// </summary>
    Task<ResolvedTenant?> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
