using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Users;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Repositories;

public sealed class TrustedDeviceRepository(IdentityTenantDbContext dbContext) : ITrustedDeviceRepository
{
    public void Add(TrustedDevice device) => dbContext.TrustedDevices.Add(device);

    public async Task<TrustedDevice?> FindUsableAsync(
        Guid userId, string tokenHash, CancellationToken cancellationToken = default)
    {
        var device = await dbContext.TrustedDevices
            .FirstOrDefaultAsync(d => d.TokenHash == tokenHash && d.UserId == userId, cancellationToken);

        return device is not null && device.IsUsable(DateTimeOffset.UtcNow) ? device : null;
    }

    public async Task<IReadOnlyList<TrustedDevice>> ListLiveForUserAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.TrustedDevices
            .Where(d => d.UserId == userId && d.RevokedAt == null)
            .ToListAsync(cancellationToken);
}

/// <summary>
/// CAPTCHA challenges. Adding rides the request's unit of work; redeeming does
/// not - see <see cref="TryRedeemAsync"/>.
/// </summary>
public sealed class CaptchaStore(
    IdentityTenantDbContext dbContext,
    IServiceScopeFactory scopeFactory,
    IPasswordHasher passwordHasher,
    IDateTimeProvider clock)
    : ICaptchaStore
{
    public void Add(CaptchaChallenge challenge) => dbContext.CaptchaChallenges.Add(challenge);

    /// <summary>
    /// Spends it on its own scope and connection, as one conditional UPDATE, so
    /// two sign-ins presenting the same solve cannot both spend it and a sign-in
    /// that fails afterwards cannot roll the spending back - the
    /// <c>FailedLoginRecorder</c> shape, for the same reason.
    /// </summary>
    public async Task<bool> TryRedeemAsync(Guid id, string answer, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();
        var now = clock.UtcNow;

        var stored = await db.CaptchaChallenges
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => c.AnswerHash)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return false;
        }

        var spent = await db.CaptchaChallenges
            .Where(c => c.Id == id && c.UsedAt == null && c.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.UsedAt, now), cancellationToken);

        if (spent != 1)
        {
            return false;
        }

        var offered = passwordHasher.HashDeterministic(CaptchaChallenge.Normalize(id, answer));

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(offered), Encoding.UTF8.GetBytes(stored));
    }

    public Task PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.UtcNow.AddHours(-1);

        return dbContext.CaptchaChallenges
            .Where(c => c.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
