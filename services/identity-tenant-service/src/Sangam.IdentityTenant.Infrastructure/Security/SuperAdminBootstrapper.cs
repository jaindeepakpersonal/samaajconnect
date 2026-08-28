using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Users;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Security;

/// <summary>
/// Creates the very first Super Admin from configuration.
/// </summary>
/// <remarks>
/// Without this there is a chicken-and-egg problem in a fresh deployment:
/// creating a Samaaj needs a Super Admin, and creating a Super Admin needs an
/// endpoint that itself requires one. It runs once, is a no-op if any Super
/// Admin already exists, and never rewrites an existing account - so it cannot
/// be used to reset a forgotten password by editing configuration.
/// </remarks>
public sealed class SuperAdminBootstrapper(
    IdentityTenantDbContext dbContext,
    IPasswordHasher passwordHasher,
    IDateTimeProvider clock,
    IOptions<BootstrapOptions> options,
    ILogger<SuperAdminBootstrapper> logger)
{
    public async Task EnsureSuperAdminAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.SuperAdminIdentifier))
        {
            logger.LogInformation("No bootstrap Super Admin configured; skipping.");
            return;
        }

        if (settings.SuperAdminPassword.Length < 10)
        {
            // Refuse rather than create a weak platform-wide account.
            logger.LogError(
                "Bootstrap:SuperAdminPassword must be at least 10 characters. No Super Admin was created.");
            return;
        }

        var alreadyExists = await dbContext.UserRoles
            .AnyAsync(ur => ur.RoleId == AuthorizationCatalog.RoleIds.SuperAdmin, cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        var identifier = User.NormalizeIdentifier(settings.SuperAdminIdentifier);

        if (await dbContext.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.MobileOrEmail == identifier, cancellationToken))
        {
            logger.LogWarning(
                "Bootstrap identifier {Identifier} is already in use by a non-admin account; skipping.",
                identifier);
            return;
        }

        var superAdmin = User.RegisterPlatformAdmin(
            identifier,
            settings.SuperAdminName,
            passwordHasher.Hash(settings.SuperAdminPassword),
            AuthorizationCatalog.RoleIds.SuperAdmin,
            clock.UtcNow);

        dbContext.Users.Add(superAdmin);

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Bootstrapped Super Admin {Identifier}. Change this password immediately.", identifier);
    }
}
