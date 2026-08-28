using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Users;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Security;

/// <summary>
/// Counts wrong activation-code guesses on its own scope and connection, so the
/// increment survives the rollback of the failing command that caused it. Same
/// shape, and the same reason, as <see cref="FailedLoginRecorder"/>.
/// </summary>
public sealed class FailedActivationRecorder(
    IServiceScopeFactory scopeFactory,
    ILogger<FailedActivationRecorder> logger)
    : IFailedActivationRecorder
{
    public async Task<bool> RecordAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .Include(u => u.ActivationCode)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user?.ActivationCode is not { } code)
        {
            return false;
        }

        code.RecordFailedAttempt();

        await dbContext.SaveChangesAsync(cancellationToken);

        var exhausted = code.FailedAttempts >= ActivationCode.MaxAttempts;

        if (exhausted)
        {
            // The code is dead, not the account: an admin issues a new one.
            logger.LogWarning(
                "Activation code for {UserId} was guessed wrong {Attempts} times and is now dead",
                userId,
                code.FailedAttempts);
        }

        return exhausted;
    }
}
