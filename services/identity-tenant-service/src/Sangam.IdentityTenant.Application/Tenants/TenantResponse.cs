namespace Sangam.IdentityTenant.Application.Tenants;

/// <summary>Full tenant record. Super Admin surfaces only.</summary>
public sealed record TenantResponse(
    Guid Id,
    string Name,
    string Slug,
    string? Domain,
    string? LogoUrl,
    string? ContactPerson,
    string? ContactEmail,
    string Status,
    IReadOnlyCollection<string> EnabledModules,
    DateTimeOffset CreatedAt);

/// <summary>
/// The public face of a tenant, returned by the anonymous slug-resolution
/// endpoint. Deliberately omits ContactPerson/ContactEmail: that endpoint is
/// reachable without a JWT, and an unauthenticated caller enumerating slugs
/// should not harvest a directory of Samaaj contact addresses.
/// </summary>
public sealed record TenantSummaryResponse(
    Guid Id,
    string Name,
    string Slug,
    string? LogoUrl,
    string Status,
    IReadOnlyCollection<string> EnabledModules);
