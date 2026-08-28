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

    /// <summary>
    /// The credential that buys the next access token. Plaintext here and
    /// nowhere else - the database keeps only a hash - and single-use: spending
    /// it returns a replacement. See RefreshToken for what happens if one is
    /// spent twice.
    /// </summary>
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
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
