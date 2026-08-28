using FluentValidation;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Users.Commands.RefreshSession;

/// <summary>
/// Exchanges a refresh token for a new access token and the next refresh token.
/// </summary>
/// <remarks>
/// Anonymous, because the refresh token <i>is</i> the credential: a caller
/// reaching this has an expired access token, or none. That is exactly why the
/// token is 256 bits of randomness and single-use.
/// </remarks>
[AllowAnonymousRequest]
public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<RefreshSessionResponse>;

public sealed record RefreshSessionResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    Guid TenantId,
    string TenantSlug,
    string FullName,
    IReadOnlyCollection<string> Roles);

public sealed class RefreshSessionCommandValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionCommandValidator()
    {
        // Length only. Anything more would be validating the shape of a secret
        // back to whoever presented it.
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }
}
