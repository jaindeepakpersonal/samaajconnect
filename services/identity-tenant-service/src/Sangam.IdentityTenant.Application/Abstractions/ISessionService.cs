using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// Starting, continuing and ending sign-in sessions.
/// </summary>
/// <remarks>
/// One place, because every path that hands out a session has to hash the token
/// the same way and every path that ends one has to revoke the whole chain. Two
/// implementations of that would drift, and the way they would drift is a
/// session that looks revoked and is not.
///
/// Nothing here saves. The caller's unit of work commits, so issuing a session
/// and recording the login that earned it land in one transaction or neither
/// does.
/// </remarks>
public interface ISessionService
{
    /// <summary>Begins a session and returns the token to hand to the caller.</summary>
    IssuedSession Begin(Guid userId, Guid tenantId);

    /// <summary>
    /// Redeems a refresh token for the next one in its chain.
    /// </summary>
    /// <remarks>
    /// A refusal is an outcome, not an exception: an expired session is an
    /// ordinary Tuesday. The reason comes back for the log and never for the
    /// caller - telling someone which of "no such token", "already used" and
    /// "expired" applies is telling them what to try next.
    /// </remarks>
    Task<SessionOutcome> ContinueAsync(
        string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the session this token belongs to. Returns how many tokens were
    /// revoked; zero means the token was unknown or the session already over,
    /// which is not an error - signing out twice should look the same as once.
    /// </summary>
    Task<int> EndAsync(
        string refreshToken,
        SessionEndReason reason,
        CancellationToken cancellationToken = default);

    /// <summary>Ends every live session for one account.</summary>
    Task<int> EndAllForUserAsync(
        Guid userId,
        SessionEndReason reason,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A session handed to a caller. <paramref name="RefreshToken"/> is plaintext
/// and is the only time it exists outside the caller's own storage - the
/// database holds a hash.
/// </summary>
public sealed record IssuedSession(
    Guid UserId,
    Guid TenantId,
    Guid SessionId,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Either a fresh session, or why there is not one. Exactly one of the two is
/// set.
/// </summary>
public sealed record SessionOutcome(IssuedSession? Session, SessionRefusal? Refusal)
{
    public static SessionOutcome Continued(IssuedSession session) => new(session, null);

    public static SessionOutcome Refused(SessionRefusal refusal) => new(null, refusal);
}

/// <summary>Why a refresh was refused. For the log; never for the caller.</summary>
public enum SessionRefusal
{
    /// <summary>No row for that token. A guess, or a session long since cleaned up.</summary>
    Unknown = 1,

    Expired = 2,
    Revoked = 3,

    /// <summary>
    /// An already-redeemed token was presented, so two parties hold it. The
    /// chain has been revoked by the time this is returned.
    /// </summary>
    ReuseDetected = 4,

    /// <summary>The account is erased, suspended, locked, or its Samaaj is not active.</summary>
    AccountUnavailable = 5,
}
