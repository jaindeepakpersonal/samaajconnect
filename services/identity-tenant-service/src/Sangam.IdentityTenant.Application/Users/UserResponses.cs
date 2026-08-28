namespace Sangam.IdentityTenant.Application.Users;

public sealed record RegisterMemberResponse(
    Guid UserId,
    Guid TenantId,
    string TenantSlug,
    string MobileOrEmail,
    bool IsContactVerified);

/// <summary>
/// <see cref="TenantSlug"/> is what the portal uses to redirect the member to
/// their Samaaj's subdomain after a common login (member-portal wireframe).
/// </summary>
public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    Guid TenantId,
    string TenantSlug,
    string FullName,
    IReadOnlyCollection<string> Roles);

public sealed record CurrentUserResponse(
    Guid UserId,
    Guid TenantId,
    string TenantSlug,
    string MobileOrEmail,
    string FullName,
    string Status,
    bool IsContactVerified,
    DateTimeOffset? LastLoginAt,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
