using System.Security.Cryptography;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A browser that has passed the second factor for one account, and is excused
/// it for thirty days.
/// </summary>
/// <remarks>
/// <para>
/// <b>It excuses the code, never the password.</b> A trusted device still signs
/// in with the password (and the CAPTCHA); what it skips is the six-digit code.
/// Stealing the device token therefore buys nothing without the password, which
/// is why it can live in the browser's <c>localStorage</c> rather than a cookie.
/// </para>
/// <para>
/// Stored as a deterministic hash, the <see cref="RefreshToken"/> shape: the
/// token is 256 random bits, so a slow salted hash would buy nothing, and a
/// deterministic one is what lets it be looked up. Revoked with the account's
/// sessions - a password change, a reset, signing out everywhere, erasure - so
/// "sign out everywhere" also means "ask everywhere for a code again".
/// </para>
/// </remarks>
public sealed class TrustedDevice
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    private TrustedDevice() { }   // EF Core

    /// <summary>Mints one. The plaintext goes to the browser and is kept nowhere here.</summary>
    public static (TrustedDevice Device, string Plaintext) Issue(
        Guid userId, Func<string, string> hashDeterministic, DateTimeOffset now)
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var device = new TrustedDevice
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hashDeterministic(plaintext),
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime),
        };

        return (device, plaintext);
    }

    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
