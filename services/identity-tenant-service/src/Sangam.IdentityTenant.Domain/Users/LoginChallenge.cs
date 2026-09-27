using System.Security.Cryptography;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// The half-finished sign-in between a correct password and the code: proof
/// that this browser got the password right a moment ago.
/// </summary>
/// <remarks>
/// <para>
/// <b>The code alone signs nobody in.</b> Verifying takes this challenge's
/// token as well as the code, and the token is issued only by a correct
/// password - so the second step cannot be reached without the first, and the
/// code is a second factor rather than a way around the password. That is also
/// why passwordless code sign-in was retired the same day this was added.
/// </para>
/// <para>
/// 256 random bits, stored as a deterministic hash so it can be looked up, and
/// living exactly as long as the code it goes with.
/// </para>
/// </remarks>
public sealed class LoginChallenge
{
    public string Hash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }

    private LoginChallenge() { }   // EF Core

    public static (LoginChallenge Challenge, string Plaintext) Issue(
        Func<string, string> hashDeterministic, DateTimeOffset now, TimeSpan lifetime)
    {
        var plaintext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return (new LoginChallenge { Hash = hashDeterministic(plaintext), ExpiresAt = now.Add(lifetime) }, plaintext);
    }

    public bool IsUsable(DateTimeOffset now) => now < ExpiresAt;
}
