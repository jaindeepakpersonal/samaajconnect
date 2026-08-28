using System.Security.Cryptography;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// One step in a sign-in session: the credential that buys a new access token.
/// </summary>
/// <remarks>
/// Access tokens are stateless by design - every service validates one without
/// calling back here - which is what makes them impossible to withdraw. A
/// refresh token is the opposite: it is a row, so it can be revoked, and it is
/// the thing that decides whether a session continues.
///
/// Stored as a hash, never as the value handed out. Same reasoning as passwords
/// and activation codes: a copy of this table must not be a set of working
/// sessions.
///
/// <b>Rotation.</b> Redeeming one issues a replacement and marks this one used.
/// A refresh token is therefore single-use, which is what makes theft
/// detectable: if a used token is presented again, two parties hold it, and one
/// of them is not the member. There is no way to tell which, so the whole
/// session is revoked and both are made to sign in again. That is the standard
/// answer and it is deliberately blunt - an inconvenience for the member, and
/// the end of the attacker's access.
///
/// <see cref="SessionId"/> ties the chain together so a reused token can revoke
/// its descendants as well as itself.
///
/// <b>This is deliberately not an <c>ITenantScopedEntity</c></b>, despite
/// carrying a tenant id. A caller redeeming one has no access token yet and so
/// no resolved tenant, exactly like the login lookup - a query filter here
/// would compare against Guid.Empty and find nothing, turning every refresh
/// into a sign-out. Nothing is lost by that: the only way to find a row is to
/// present its 256-bit secret, and the tenant on the row is what the new access
/// token is scoped to.
/// </remarks>
public sealed class RefreshToken
{
    /// <summary>
    /// How long a session survives without being used. Long enough that a
    /// member who opens the app most days is never signed out; short enough
    /// that a device found in a drawer is not a live session.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>Bytes of entropy in the token handed out.</summary>
    private const int TokenBytes = 32;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>
    /// The chain this token belongs to. Every rotation keeps it, so revoking a
    /// session means revoking every token that shares this id.
    /// </summary>
    public Guid SessionId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Set when this token was redeemed and replaced.</summary>
    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Why the session ended. Read in support, and in the audit log.</summary>
    public SessionEndReason? RevokedReason { get; private set; }

    private RefreshToken() { }   // EF Core

    /// <summary>
    /// Starts a new session. <paramref name="sessionId"/> continues an existing
    /// chain; omit it to begin one.
    /// </summary>
    public static (RefreshToken Token, string Plaintext) Issue(
        Guid userId,
        Guid tenantId,
        Func<string, string> hash,
        DateTimeOffset now,
        Guid? sessionId = null)
    {
        // URL-safe base64 so the value survives a JSON body and a header
        // without escaping, and 256 bits so guessing is not a strategy.
        var plaintext = Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            SessionId = sessionId ?? Guid.NewGuid(),
            TokenHash = hash(plaintext),
            IssuedAt = now,
            ExpiresAt = now.Add(Lifetime),
        };

        return (token, plaintext);
    }

    /// <summary>Usable means not yet redeemed, not revoked, and not expired.</summary>
    public bool IsUsable(DateTimeOffset now) =>
        UsedAt is null && RevokedAt is null && ExpiresAt > now;

    /// <summary>
    /// True when this token was already redeemed. Presented again, that is the
    /// signal that someone else has a copy.
    /// </summary>
    public bool IsReplayed(DateTimeOffset now) => UsedAt is not null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;

    public void Revoke(SessionEndReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public enum SessionEndReason
{
    /// <summary>The member signed out.</summary>
    SignedOut = 1,

    /// <summary>An already-redeemed token was presented, so someone has a copy.</summary>
    ReuseDetected = 2,

    /// <summary>The account was erased.</summary>
    AccountErased = 3,

    /// <summary>An administrator ended it, or the account was locked.</summary>
    EndedByAdministrator = 4,

    /// <summary>The member changed their password.</summary>
    PasswordChanged = 5,
}
