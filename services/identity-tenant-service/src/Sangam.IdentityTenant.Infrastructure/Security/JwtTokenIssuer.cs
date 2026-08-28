using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sangam.IdentityTenant.Application.Abstractions;

namespace Sangam.IdentityTenant.Infrastructure.Security;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, IDateTimeProvider clock) : ITokenIssuer
{
    public AccessToken Issue(
        Guid userId,
        Guid tenantId,
        string mobileOrEmail,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions)
    {
        var jwt = options.Value;
        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(PlatformClaimTypes.TenantId, tenantId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, mobileOrEmail),
        };

        // Permissions are embedded rather than looked up per request so a
        // downstream service can authorize without calling back here. The cost
        // is that a revoked permission stays valid until the token expires,
        // which is why the lifetime is short.
        claims.AddRange(roles.Select(role => new Claim(PlatformClaimTypes.Role, role)));
        claims.AddRange(permissions.Select(permission => new Claim(PlatformClaimTypes.Permission, permission)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
