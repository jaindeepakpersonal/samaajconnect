using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sangam.SocialIssues.Infrastructure.Security;

namespace Sangam.SocialIssues.Api.Extensions;

public static class AuthenticationExtensions
{
    /// <summary>
    /// Validates incoming JWTs. Every service does this, not just the gateway:
    /// the gateway is a filter, not the authorization boundary
    /// (ARCHITECTURE.md section 6). JwtOptions itself is bound and validated by
    /// AddInfrastructure, which also owns token issuance.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
    {
        services.ConfigureOptions<ConfigureJwtBearerOptions>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        return services;
    }
}

/// <summary>
/// Binds the validation parameters at resolution time rather than at
/// registration time, so configuration sources added after service registration
/// still apply.
/// </summary>
internal sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name is not null && name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var jwt = jwtOptions.Value;

        // Off, so a claim arrives named as it was issued. Left on, the handler
        // rewrites "role" to the long WS-Federation URI and every role check
        // downstream silently stops matching.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = PlatformClaimTypes.Role,
            NameClaimType = "sub",
        };
    }
}
