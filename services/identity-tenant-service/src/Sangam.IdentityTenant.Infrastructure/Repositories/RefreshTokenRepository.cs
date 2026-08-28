using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Users;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Repositories;

public sealed class RefreshTokenRepository(IdentityTenantDbContext dbContext) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByHashAsync(
        string tokenHash, CancellationToken cancellationToken = default) =>
        dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> ListForSessionAsync(
        Guid sessionId, CancellationToken cancellationToken = default) =>
        await dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.SessionId == sessionId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> ListLiveForUserAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(RefreshToken token) => dbContext.RefreshTokens.Add(token);

    public Task<int> DeleteExpiredBeforeAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.ExpiresAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
}
