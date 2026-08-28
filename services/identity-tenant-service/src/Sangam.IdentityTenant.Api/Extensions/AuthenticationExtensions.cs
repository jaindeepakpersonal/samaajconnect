using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Sangam.IdentityTenant.Api.Extensions;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "samaajconnect";

    public string Audience { get; set; } = "samaajconnect";

    /// <summary>HS256 signing key. Must be at least 32 characters.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;
}

public static class AuthenticationExtensions
{
    /// <summary>
    /// Validates incoming JWTs. Every service does this, not just the gateway:
    /// the gateway is a filter, not the authorization boundary
    /// (ARCHITECTURE.md section 6).
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.SigningKey) && options.SigningKey.Length >= 32,
                "Jwt:SigningKey must be configured and at least 32 characters long.")
            .ValidateOnStart();

        services.ConfigureOptions<ConfigureJwtBearerOptions>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        return services;
    }
}

/// <summary>
/// Binds the validation parameters from <see cref="JwtOptions"/> at resolution
/// time rather than at registration time, so configuration sources added after
/// service registration still apply.
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
            RoleClaimType = "role",
            NameClaimType = "sub",
        };
    }
}
