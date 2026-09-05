using System.Security.Cryptography;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A one-time code a member can use instead of a password.
/// </summary>
/// <remarks>
/// Stored as a hash, the same way <see cref="ActivationCode"/> is, and hashed
/// with the same slow, per-value-salted <c>IPasswordHasher.Hash</c> - never
/// <c>HashDeterministic</c>, which is reserved for a high-entropy secret this
/// six-digit code is not.
///
/// Deliberately has no attempt counter of its own, unlike
/// <see cref="ActivationCode"/>. SECURITY-CHECKLIST.md says to treat "any OTP
/// endpoint" as "any endpoint that checks a credential" - so a wrong code
/// counts against the account's existing login lockout
/// (<see cref="User.IsLockedOut"/>/<see cref="User.RecordFailedLogin"/>, the
/// same one a wrong password already trips) rather than a second, parallel
/// counter. An activation code needs its own because a
/// <see cref="UserStatus.PendingActivation"/> account has no password and
/// no login lockout to share; an account requesting a sign-in code is
/// already <see cref="UserStatus.Active"/> and already has one.
/// </remarks>
public sealed class LoginOtp
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string Hash { get; private set; } = null!;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    private LoginOtp() { }

    /// <summary>
    /// Mints a code. The plaintext is returned to the caller and is not kept
    /// anywhere on this object - it exists only long enough to be handed to
    /// the notification pipeline.
    /// </summary>
    public static (LoginOtp Code, string Plaintext) Issue(Func<string, string> hasher, DateTimeOffset now)
    {
        var plaintext = Generate();

        var code = new LoginOtp
        {
            Hash = hasher(plaintext),
            IssuedAt = now,
            ExpiresAt = now.Add(Lifetime),
        };

        return (code, plaintext);
    }

    public bool IsUsable(DateTimeOffset now) => now < ExpiresAt;

    /// <summary>
    /// A real six-digit code, matching the wireframe's own "6-digit code"
    /// placeholder. <see cref="RandomNumberGenerator"/>, not <c>Random</c> -
    /// this is a credential, not a display value.
    /// </summary>
    private static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
