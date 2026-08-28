using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Infrastructure.Messaging;
using Sangam.AuditNotification.Infrastructure.Persistence;
using Sangam.AuditNotification.Infrastructure.Repositories;
using Sangam.AuditNotification.Infrastructure.Security;

namespace Sangam.AuditNotification.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration is read inside the callback, not here: sources added
        // after registration (a test host pointing at a throwaway container,
        // a secret provider) must still win.
        services.AddDbContext<AuditNotificationDbContext>((serviceProvider, options) =>
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
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IAuditLogQueries, AuditLogQueries>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(
                jwt => !string.IsNullOrWhiteSpace(jwt.SigningKey) && jwt.SigningKey.Length >= 32,
                "Jwt:SigningKey must be configured and at least 32 characters long.")
            .ValidateOnStart();

        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.Configure<ConsumerOptions>(configuration.GetSection(ConsumerOptions.SectionName));

        services.AddSingleton<IEventPublisher, KafkaProducer>();
        services.AddHostedService<OutboxDispatcher>();

        // The point of this service: one consumer, wired end to end.
        services.AddHostedService<IntegrationEventConsumer>();

        return services;
    }
}
