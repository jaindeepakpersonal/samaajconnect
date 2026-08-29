using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Infrastructure.Messaging;
using Sangam.Events.Infrastructure.Persistence;
using Sangam.Events.Infrastructure.Repositories;
using Sangam.Events.Infrastructure.Security;

namespace Sangam.Events.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration is read inside the callback so sources added after
        // registration - a test host, a secret provider - still win.
        services.AddDbContext<EventsDbContext>((serviceProvider, options) =>
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
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                jwt => !string.IsNullOrWhiteSpace(jwt.SigningKey) && jwt.SigningKey.Length >= 32,
                "Jwt:SigningKey must be configured and at least 32 characters long.")
            .ValidateOnStart();

        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        services.AddSingleton<IEventPublisher, KafkaProducer>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
