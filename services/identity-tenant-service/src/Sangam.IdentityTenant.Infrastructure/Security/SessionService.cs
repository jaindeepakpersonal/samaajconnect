using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sangam.IdentityTenant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Infrastructure.Security;

/// <summary>
/// The one implementation of starting, continuing and ending a session. See
/// <see cref="ISessionService"/> for why there is only one.
/// </summary>
public sealed class SessionService(
    IRefreshTokenRepository tokens,
    IUserRepository users,
    ITenantRepository tenants,
    IPasswordHasher hasher,
    IServiceScopeFactory scopeFactory,
    IDateTimeProvider clock,
    ILogger<SessionService> logger)
    : ISessionService
{
    public IssuedSession Begin(Guid userId, Guid tenantId)
    {
        var (token, plaintext) = RefreshToken.Issue(userId, tenantId, Hash, clock.UtcNow);

        tokens.Add(token);

        return new IssuedSession(userId, tenantId, token.SessionId, plaintext, token.ExpiresAt);
    }

    public async Task<SessionOutcome> ContinueAsync(
        string refreshToken, CancellationToken cancellationToken = default)
    {
        var existing = await tokens.FindByHashAsync(Hash(refreshToken), cancellationToken);
        var now = clock.UtcNow;

        if (existing is null)
        {
            return SessionOutcome.Refused(SessionRefusal.Unknown);
        }

        // The theft signal. A refresh token is single-use, so a second
        // presentation means two parties hold it and one is not the member.
        // There is no way to tell which, so the whole chain goes and both are
        // made to sign in again.
        if (existing.IsReplayed(now))
        {
            var revoked = await RevokeSessionOutOfBandAsync(
                existing.SessionId, SessionEndReason.ReuseDetected, cancellationToken);

            logger.LogWarning(
                "Refresh token reuse on session {SessionId}: revoked {Count} token(s). "
                + "Someone other than the member is holding a copy.",
                existing.SessionId,
                revoked);

            return SessionOutcome.Refused(SessionRefusal.ReuseDetected);
        }

        if (existing.RevokedAt is not null)
        {
            return SessionOutcome.Refused(SessionRefusal.Revoked);
        }

        if (!existing.IsUsable(now))
        {
            return SessionOutcome.Refused(SessionRefusal.Expired);
        }

        // The account and its Samaaj are re-checked on every refresh, which is
        // what makes suspending an account or deactivating a Samaaj actually
        // bite: the access token cannot be withdrawn, but it expires, and this
        // is the gate it has to come back through.
        var user = await users.GetSelfAsync(existing.UserId, cancellationToken);

        if (user is null || user.Status != UserStatus.Active)
        {
            await RevokeSessionOutOfBandAsync(
                existing.SessionId, SessionEndReason.EndedByAdministrator, cancellationToken);

            return SessionOutcome.Refused(SessionRefusal.AccountUnavailable);
        }

        if (!user.IsPlatformAdministrator)
        {
            var tenant = await tenants.GetByIdAsync(user.TenantId, cancellationToken);

            if (tenant is null || tenant.Status != TenantStatus.Active)
            {
                return SessionOutcome.Refused(SessionRefusal.AccountUnavailable);
            }
        }

        existing.MarkUsed(now);

        var (next, plaintext) = RefreshToken.Issue(
            existing.UserId, existing.TenantId, Hash, now, existing.SessionId);

        tokens.Add(next);

        return SessionOutcome.Continued(new IssuedSession(
            existing.UserId, existing.TenantId, existing.SessionId, plaintext, next.ExpiresAt));
    }

    public async Task<int> EndAsync(
        string refreshToken,
        SessionEndReason reason,
        CancellationToken cancellationToken = default)
    {
        var existing = await tokens.FindByHashAsync(Hash(refreshToken), cancellationToken);

        if (existing is null)
        {
            // Signing out with a token nobody recognises is not an error, and
            // saying so would confirm which tokens exist.
            return 0;
        }

        return await RevokeSessionAsync(existing.SessionId, reason, cancellationToken);
    }

    public async Task<int> EndAllForUserAsync(
        Guid userId,
        SessionEndReason reason,
        CancellationToken cancellationToken = default)
    {
        var live = await tokens.ListLiveForUserAsync(userId, cancellationToken);
        var now = clock.UtcNow;

        foreach (var token in live)
        {
            token.Revoke(reason, now);
        }

        return live.Count;
    }

    private async Task<int> RevokeSessionAsync(
        Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken)
    {
        var chain = await tokens.ListForSessionAsync(sessionId, cancellationToken);
        var now = clock.UtcNow;
        var revoked = 0;

        foreach (var token in chain.Where(t => t.RevokedAt is null))
        {
            token.Revoke(reason, now);
            revoked++;
        }

        return revoked;
    }

    /// <summary>
    /// Revokes a session on its own scope, context and connection, so the write
    /// survives the rollback of the request that discovered the problem.
    /// </summary>
    /// <remarks>
    /// The same trap <see cref="IFailedLoginRecorder"/> exists for. Refreshing
    /// is a command, so TransactionBehavior wraps it and rolls back whenever the
    /// handler returns a failure - and detecting a stolen token returns a
    /// failure. Revoking on the ambient context would therefore be undone by
    /// the very request that found the theft, leaving the attacker's session
    /// live and a log line saying it had been closed.
    /// </remarks>
    private async Task<int> RevokeSessionOutOfBandAsync(
        Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityTenantDbContext>();

        var chain = await dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.SessionId == sessionId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;

        foreach (var token in chain)
        {
            token.Revoke(reason, now);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return chain.Count;
    }

    /// <summary>
    /// Reuses the password hasher, as activation codes do: the stored form of a
    /// bearer secret has the same requirements as a stored password, and one
    /// slow salted hash in the codebase is easier to keep correct than two.
    /// </summary>
    /// <remarks>
    /// The lookup is by hash, so the hash has to be deterministic - which a
    /// per-value salt is not. <see cref="IPasswordHasher.HashDeterministic"/>
    /// exists for exactly this, and is safe here in a way it would not be for a
    /// password: the input is 256 bits of cryptographic randomness, so there is
    /// no dictionary to build and nothing for a salt to defend against.
    /// </remarks>
    private string Hash(string value) => hasher.HashDeterministic(value);
}
