namespace Sangam.IdentityTenant.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>
    /// Verifies a password. Implementations must compare in constant time and
    /// must not short-circuit on a malformed stored hash.
    /// </summary>
    bool Verify(string password, string passwordHash);

    /// <summary>
    /// A salt-free, deterministic hash, for secrets that have to be *looked up*
    /// by their hash rather than verified against a known row.
    /// </summary>
    /// <remarks>
    /// Refresh tokens are the case: the only thing a caller presents is the
    /// token, so the query is "find the row whose hash is this", and a per-value
    /// salt makes that impossible.
    ///
    /// Dropping the salt is safe here and would not be for a password. A salt
    /// defends against precomputation over a space someone can guess; the input
    /// here is 256 bits of cryptographic randomness, so there is no dictionary
    /// to precompute and nothing to be gained from a rainbow table. It is also
    /// why this is fast rather than deliberately slow: slowness buys nothing
    /// against an input nobody can enumerate.
    ///
    /// <b>Never pass a password, a code a human typed, or anything else with
    /// guessable structure to this.</b>
    /// </remarks>
    string HashDeterministic(string value);
}
