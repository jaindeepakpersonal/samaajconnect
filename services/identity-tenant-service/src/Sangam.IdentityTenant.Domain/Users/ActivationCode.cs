using System.Security.Cryptography;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A one-time code a Samaaj admin hands to a new member so they can set their
/// first password.
/// </summary>
/// <remarks>
/// Stored as a hash, never as text. There is no notification channel on the
/// platform yet, so the plaintext is returned to the issuing admin exactly once
/// and passed on in person - which is realistic for a community organisation
/// and, unlike an emailed link, involves no channel that can be intercepted.
/// The stored form is a hash so that a database copy does not become a set of
/// working credentials.
/// </remarks>
public sealed class ActivationCode
{
    /// <summary>
    /// Excludes 0/O and 1/I/L. This code is read aloud or written on paper,
    /// which is exactly when those characters get confused.
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private const int Length = 10;

    /// <summary>Wrong guesses before the code is dead and a new one is needed.</summary>
    public const int MaxAttempts = 5;

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public string Hash { get; private set; } = null!;
    public DateTimeOffset IssuedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid IssuedBy { get; private set; }
    public int FailedAttempts { get; private set; }

    private ActivationCode() { }

    /// <summary>
    /// Mints a code. The plaintext is returned to the caller and is not kept
    /// anywhere on this object.
    /// </summary>
    public static (ActivationCode Code, string Plaintext) Issue(
        Guid issuedBy, Func<string, string> hasher, DateTimeOffset now)
    {
        var plaintext = Generate();

        var code = new ActivationCode
        {
            Hash = hasher(plaintext),
            IssuedAt = now,
            ExpiresAt = now.Add(Lifetime),
            IssuedBy = issuedBy,
        };

        return (code, plaintext);
    }

    public bool IsUsable(DateTimeOffset now) => now < ExpiresAt && FailedAttempts < MaxAttempts;

    /// <summary>Records a wrong guess. Five kills the code rather than the account.</summary>
    public void RecordFailedAttempt() => FailedAttempts++;

    private static string Generate() =>
        string.Concat(
            Enumerable.Range(0, Length)
                .Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]));
}
