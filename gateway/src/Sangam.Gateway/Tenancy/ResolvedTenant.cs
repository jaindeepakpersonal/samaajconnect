namespace Sangam.Gateway.Tenancy;

/// <summary>
/// What the gateway needs to know about a Samaaj to route a request: who it is,
/// whether it is open for business, and which modules it runs.
/// </summary>
public sealed record ResolvedTenant(
    Guid Id,
    string Slug,
    string Status,
    IReadOnlyCollection<string> EnabledModules)
{
    public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

    public bool HasModule(string moduleKey) =>
        EnabledModules.Contains(moduleKey, StringComparer.OrdinalIgnoreCase);
}
