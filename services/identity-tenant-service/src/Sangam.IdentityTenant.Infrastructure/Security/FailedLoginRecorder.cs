using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Security;

/// <summary>
/// Increments the failed-attempt counter on its own scope, context and
/// connection, so the write survives the rollback of the failing login command
/// that triggered it. See <see cref="IFailedLoginRecorder"/> for why that
/// matters.
/// </summary>
public sealed class FailedLoginRecorder(
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider clock,
    ILogger<FailedLoginRecorder> logger)
    : IFailedLoginRecorder
{
    public async Task<bool> RecordAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return false;
        }

        var lockedOut = user.RecordFailedLogin(clock.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (lockedOut)
        {
            logger.LogWarning("Account {UserId} locked out after repeated failed logins", userId);
        }

        return lockedOut;
    }
}
