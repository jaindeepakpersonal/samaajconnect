using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Infrastructure.Messaging;
using Sangam.Pathshala.Infrastructure.Persistence;
using Sangam.Pathshala.Infrastructure.Repositories;
using Sangam.Pathshala.Infrastructure.Security;

namespace Sangam.Pathshala.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration is read inside the callback so sources added after
        // registration - a test host, a secret provider - still win.
        services.AddDbContext<PathshalaDbContext>((serviceProvider, options) =>
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
        services.AddScoped<IPathshalaRepository, PathshalaRepository>();
        services.AddScoped<IEnrolmentRepository, EnrolmentRepository>();
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

        // The one thing this service reacts to: a child profile becoming an
        // account, so their enrolments can be read by the person they now are.
        services.AddHostedService<IntegrationEventConsumer>();

        return services;
    }
}
