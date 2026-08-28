using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Infrastructure.Messaging;
using Sangam.IdentityTenant.Infrastructure.Persistence;
using Sangam.IdentityTenant.Infrastructure.Repositories;

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
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));

        services.AddSingleton<IEventPublisher, KafkaProducer>();
        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}
