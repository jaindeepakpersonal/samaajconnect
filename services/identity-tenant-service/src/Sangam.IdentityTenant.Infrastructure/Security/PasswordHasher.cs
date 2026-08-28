using System.Security.Cryptography;
using Sangam.IdentityTenant.Application.Abstractions;

namespace Sangam.IdentityTenant.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256. Chosen over Argon2/bcrypt only because it is in the base
/// class library - no extra dependency to audit - and the iteration count is
/// stored per hash so it can be raised later without invalidating existing
/// passwords.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int DefaultIterations = 210_000;
    private const string Prefix = "pbkdf2-sha256";

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, Algorithm, KeySize);

        return string.Join('$', Prefix, DefaultIterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        var parts = passwordHash.Split('$');

        // A malformed stored hash is a failed verification, never an exception:
        // throwing here would turn one corrupt row into a 500 on every attempt.
        if (parts.Length != 4
            || parts[0] != Prefix
            || !int.TryParse(parts[1], out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
