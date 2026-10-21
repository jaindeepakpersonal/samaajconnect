using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Infrastructure.Messaging;
using Sangam.Panch.Infrastructure.Persistence;
using Sangam.Panch.Infrastructure.Repositories;
using Sangam.Panch.Infrastructure.Security;

namespace Sangam.Panch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration is read inside the callback so sources added after
        // registration - a test host, a secret provider - still win.
        services.AddDbContext<PanchDbContext>((serviceProvider, options) =>
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
        services.AddScoped<IElectionRepository, ElectionRepository>();
        services.AddScoped<IBallotRepository, BallotRepository>();
        services.AddScoped<IPanelRepository, PanelRepository>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                jwt => !string.IsNullOrWhiteSpace(jwt.SigningKey) && jwt.SigningKey.Length >= 32,
                "Jwt:SigningKey must be configured and at least 32 characters long.")
            .Validate(
                jwt => JwtOptions.IsAllowedHere(jwt.SigningKey),
                "Jwt:SigningKey is the published local-development key, which only a Development environment may use. Inject a real key.")
            .ValidateOnStart();

        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        services.AddSingleton<IEventPublisher, KafkaProducer>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
