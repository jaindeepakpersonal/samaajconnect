using System.Security.Cryptography;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A one-time code that lets someone who cannot sign in prove they hold the
/// account's own contact address, and set a new password.
/// </summary>
/// <remarks>
/// Identical in shape to <see cref="LoginOtp"/>, for the same reasons: hashed
/// with <c>IPasswordHasher.Hash</c>, never <c>HashDeterministic</c>; a
/// 10-minute lifetime; and deliberately no attempt counter of its own - a
/// wrong guess counts against the account's existing login lockout instead,
/// per SECURITY-CHECKLIST.md's "read any OTP endpoint as any endpoint that
/// checks a credential". A separate type rather than reusing
/// <see cref="LoginOtp"/> because the two answer different questions - one
/// authenticates, the other authorises a password change - and this
/// platform's own convention is a distinctly named type per distinct real
/// thing rather than one type wearing two hats.
/// </remarks>
public sealed class PasswordResetCode
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string Hash { get; private set; } = null!;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    private PasswordResetCode() { }

    public static (PasswordResetCode Code, string Plaintext) Issue(Func<string, string> hasher, DateTimeOffset now)
    {
        var plaintext = Generate();

        var code = new PasswordResetCode
        {
            Hash = hasher(plaintext),
            IssuedAt = now,
            ExpiresAt = now.Add(Lifetime),
        };

        return (code, plaintext);
    }

    public bool IsUsable(DateTimeOffset now) => now < ExpiresAt;

    private static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}
