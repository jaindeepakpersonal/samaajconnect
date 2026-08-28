using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Sangam.Gateway.Tenancy;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.SectionName));
builder.Services.AddSingleton<HostSlugExtractor>();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var gatewayOptions = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>()
    ?? new GatewayOptions();

builder.Services.AddHttpClient(CachedTenantResolver.HttpClientName, client =>
{
    client.BaseAddress = new Uri(gatewayOptions.IdentityServiceUrl);

    // Short on purpose: slug resolution sits in front of every request, so a
    // slow identity service must fail fast into a 502 rather than hold the
    // whole gateway open.
    client.Timeout = TimeSpan.FromSeconds(5);
});

// Redis is optional by design. Unconfigured or unreachable, the gateway still
// resolves tenants - just without a cache.
var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
IConnectionMultiplexer? redis = null;

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    try
    {
        var redisConfiguration = ConfigurationOptions.Parse(redisConnectionString);

        // Do not abort on a failed first connect: Redis coming up after the
        // gateway should heal on its own rather than need a restart.
        redisConfiguration.AbortOnConnectFail = false;

        redis = ConnectionMultiplexer.Connect(redisConfiguration);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            "Could not connect to Redis; tenant lookups will not be cached. " + exception.Message);
    }
}

if (redis is null)
{
    builder.Services.AddSingleton<ITenantCache, NullTenantCache>();
}
else
{
    builder.Services.AddSingleton(redis);
    builder.Services.AddSingleton<ITenantCache, RedisTenantCache>();
}

builder.Services.AddSingleton<ITenantResolver, CachedTenantResolver>();

var signingKey = builder.Configuration["Jwt:SigningKey"];

if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
{
    // Fail at boot rather than validate every token against a guessable key.
    throw new InvalidOperationException(
        "Jwt:SigningKey must be configured and at least 32 characters long.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "samaajconnect",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "samaajconnect",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "role",
            NameClaimType = "sub",
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Authentication runs before tenant resolution because the override check needs
// to know whether the caller is a Super Admin. It authorizes nothing: every
// service re-checks roles and permissions itself, and the gateway is a filter
// rather than the authorization boundary (ARCHITECTURE.md section 6).
app.UseAuthentication();

app.UseMiddleware<TenantResolutionMiddleware>();

app.MapReverseProxy(proxyPipeline =>
{
    // Inside the proxy pipeline, where YARP has already selected the route and
    // its metadata - which is where the module key lives.
    proxyPipeline.UseMiddleware<ModuleGateMiddleware>();
});

await app.RunAsync();

/// <summary>Exposed so the gateway tests can host this exact composition root.</summary>
public partial class Program;
