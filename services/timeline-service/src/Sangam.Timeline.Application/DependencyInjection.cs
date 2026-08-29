using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Sangam.Timeline.Application.Behaviors;

namespace Sangam.Timeline.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR, the validators, and the five pipeline behaviors.
    /// Registration order below <b>is</b> execution order (CLAUDE.md §4.4) —
    /// it is load-bearing, not cosmetic. Do not reorder without updating
    /// CLAUDE.md §4.4 and every service's registration together.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));               // 1
            cfg.AddOpenBehavior(typeof(TenantAuthorizationBehavior<,>));   // 2
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));            // 3
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));           // 4
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehavior<,>));    // 5
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
