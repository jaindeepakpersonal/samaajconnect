namespace Sangam.IdentityTenant.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifies a password. Implementations must compare in constant time and
    /// must not short-circuit on a malformed stored hash.
    /// </summary>
    bool Verify(string password, string passwordHash);
}
