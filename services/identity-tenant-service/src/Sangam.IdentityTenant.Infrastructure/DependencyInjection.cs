using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Infrastructure.Messaging;
using Sangam.IdentityTenant.Infrastructure.Persistence;
using Sangam.IdentityTenant.Infrastructure.Repositories;
using Sangam.IdentityTenant.Infrastructure.Security;

namespace Sangam.IdentityTenant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration is read inside the callback, not here: sources added
        // after registration (a test host pointing at a throwaway container,
        // a secret provider) must still win.
        services.AddDbContext<IdentityTenantDbContext>((serviceProvider, options) =>
        {
            var connectionString = serviceProvider
                .GetRequiredService<IConfiguration>()
                .GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Default is not configured. See CLAUDE.md section 8.");

            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history"));
            options.UseSnakeCaseNamingConvention();
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantSecurityPolicyRepository, TenantSecurityPolicyRepository>();
        services.AddScoped<ISecurityPolicyResolver, SecurityPolicyResolver>();
        services.AddScoped<ITenantImageStore, TenantImageStore>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuthorizationCatalogRepository, AuthorizationCatalogRepository>();
        services.AddScoped<IConsentRepository, ConsentRepository>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenIssuer, JwtTokenIssuer>();
        services.AddScoped<IFailedLoginRecorder, FailedLoginRecorder>();
        services.AddScoped<IDataExportRecorder, DataExportRecorder>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ITrustedDeviceRepository, TrustedDeviceRepository>();
        services.AddScoped<ICaptchaStore, CaptchaStore>();
        services.AddSingleton<ICaptchaRenderer, CaptchaRenderer>();

        // Read when first asked for, so a host's (or a test's) configuration is final by then.
        services.AddSingleton(sp =>
        {
            var signIn = sp.GetRequiredService<IConfiguration>()
                .GetSection(SignInSecurityOptions.SectionName).Get<SignInSecurityOptions>() ?? new SignInSecurityOptions();

            // Revealing the CAPTCHA answers and sign-in codes switches both
            // guards off for anybody who can read the log or the response. On
            // the compose stack that is the point; anywhere else it would
            // quietly make two-factor sign-in one-factor. Program.cs resolves
            // this at start-up, so a host configured that way refuses to start.
            if (signIn.RevealSecretsInLogs && !sp.GetRequiredService<IHostEnvironment>().IsDevelopment())
            {
                throw new InvalidOperationException(
                    "SignInSecurity:RevealSecretsInLogs is for the local compose stack only and may not be on "
                    + "outside a Development environment.");
            }

            return signIn;
        });
        services.AddScoped<IFailedActivationRecorder, FailedActivationRecorder>();

        // Bound and validated here rather than in Api: this service issues
        // tokens as well as validating them, and both halves must agree.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                jwt => !string.IsNullOrWhiteSpace(jwt.SigningKey) && jwt.SigningKey.Length >= 32,
                "Jwt:SigningKey must be configured and at least 32 characters long.")
            .Validate(
                jwt => JwtOptions.IsAllowedHere(jwt.SigningKey),
                "Jwt:SigningKey is the published local-development key, which only a Development environment may use. Inject a real key.")
            .ValidateOnStart();

        services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.SectionName));
        services.AddScoped<SuperAdminBootstrapper>();

        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        services.AddSingleton<IEventPublisher, KafkaProducer>();
        services.Configure<ConsumerOptions>(configuration.GetSection(ConsumerOptions.SectionName));

        services.AddHostedService<OutboxDispatcher>();

        // This service's first consumer. It publishes far more than it consumes,
        // but an approved child conversion is decided in member-family-service
        // and the account it implies can only be created here.
        services.AddHostedService<IntegrationEventConsumer>();

        return services;
    }
}
