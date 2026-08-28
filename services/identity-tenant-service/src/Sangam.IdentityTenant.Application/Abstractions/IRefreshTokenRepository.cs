using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Abstractions;

public interface IRefreshTokenRepository
{
    /// <summary>
    /// Finds a token by its hash, across tenants.
    /// </summary>
    /// <remarks>
    /// Bypasses the tenant query filter deliberately, and for the same reason
    /// the login lookup does: a caller redeeming a refresh token has no access
    /// token yet, so there is no tenant on the request to filter by. The tenant
    /// is <i>derived</i> from the row that is found, never supplied by the
    /// caller - and the lookup is by a 256-bit secret, so finding a row at all
    /// is the proof of identity.
    /// </remarks>
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>Every token in one session chain, including the used ones.</summary>
    Task<IReadOnlyList<RefreshToken>> ListForSessionAsync(
        Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>Every live token for one account, across all their sessions.</summary>
    Task<IReadOnlyList<RefreshToken>> ListLiveForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    void Add(RefreshToken token);

    /// <summary>
    /// Deletes tokens that expired long enough ago to be of no forensic use.
    /// Returns how many went.
    /// </summary>
    Task<int> DeleteExpiredBeforeAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
