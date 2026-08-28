namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// Mints tenant-scoped access tokens. Only identity-tenant-service implements
/// this; every other service validates tokens but never issues them
/// (SERVICES.md).
/// </summary>
public interface ITokenIssuer
{
    AccessToken Issue(
        Guid userId,
        Guid tenantId,
        string mobileOrEmail,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);
