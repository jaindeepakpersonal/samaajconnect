using FluentAssertions;
using Sangam.IdentityTenant.Infrastructure.Security;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void A_different_password_does_not_verify()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("Correct horse battery staple", hash).Should().BeFalse();
    }

    [Fact]
    public void Hashing_the_same_password_twice_gives_different_hashes()
    {
        // Per-hash salt: two members who pick the same password must not be
        // visibly identical in the users table.
        _hasher.Hash("same password").Should().NotBe(_hasher.Hash("same password"));
    }

    [Fact]
    public void The_stored_hash_records_its_own_iteration_count_so_it_can_be_raised_later()
    {
        _hasher.Hash("whatever").Split('$').Should().HaveCount(4)
            .And.HaveElementAt(0, "pbkdf2-sha256");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("pbkdf2-sha256$notanumber$c2FsdA==$a2V5")]
    [InlineData("pbkdf2-sha256$0$c2FsdA==$a2V5")]
    [InlineData("pbkdf2-sha256$1000$!!!not-base64!!!$a2V5")]
    [InlineData("bcrypt$1000$c2FsdA==$a2V5")]
    public void A_malformed_stored_hash_fails_verification_rather_than_throwing(string storedHash)
    {
        // One corrupt row must not turn every login attempt into a 500.
        var act = () => _hasher.Verify("any password", storedHash);

        act.Should().NotThrow();
        _hasher.Verify("any password", storedHash).Should().BeFalse();
    }

    [Fact]
    public void An_empty_password_never_verifies()
    {
        _hasher.Verify("", _hasher.Hash("real password")).Should().BeFalse();
    }
}
