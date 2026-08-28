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
    DateTimeOffset CreatedAt,
    GrievanceContactResponse? GrievanceContact);

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
    IReadOnlyCollection<string> EnabledModules,
    GrievanceContactResponse? GrievanceContact);

/// <summary>
/// Who to complain to about data handling. Public on purpose: DPDP section 13
/// requires the means of grievance redressal to be published, and a contact
/// only members can see is not published.
/// </summary>
public sealed record GrievanceContactResponse(string? Name, string? Email, string? Phone);
