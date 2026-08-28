using FluentAssertions;
using Sangam.IdentityTenant.Application.Common;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class ErrorTests
{
    [Fact]
    public void Two_errors_with_the_same_code_and_message_are_equal()
    {
        // Handlers deliberately return one identical error from several paths
        // (an unknown account and a wrong password, for instance). If equality
        // fell back to reference comparison on the field-error dictionary,
        // those paths would look different to anything comparing them.
        Error.Unauthorized("Auth.InvalidCredentials", "Incorrect mobile/email or password.")
            .Should().Be(Error.Unauthorized("Auth.InvalidCredentials", "Incorrect mobile/email or password."));
    }

    [Fact]
    public void Errors_differing_only_by_code_are_not_equal()
    {
        Error.Failure("A", "same message").Should().NotBe(Error.Failure("B", "same message"));
    }

    [Fact]
    public void Errors_differing_only_by_type_are_not_equal()
    {
        Error.NotFound("X", "message").Should().NotBe(Error.Conflict("X", "message"));
    }

    [Fact]
    public void Validation_errors_compare_their_field_messages()
    {
        var left = Error.Validation(new Dictionary<string, string[]> { ["Slug"] = ["required"] });
        var same = Error.Validation(new Dictionary<string, string[]> { ["Slug"] = ["required"] });
        var different = Error.Validation(new Dictionary<string, string[]> { ["Slug"] = ["too long"] });

        left.Should().Be(same);
        left.Should().NotBe(different);
    }

    [Fact]
    public void Equal_errors_hash_the_same()
    {
        var left = Error.Conflict("Tenant.SlugTaken", "taken");
        var right = Error.Conflict("Tenant.SlugTaken", "taken");

        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void A_real_error_is_never_equal_to_Error_None()
    {
        Error.Failure("X", "y").Should().NotBe(Error.None);
    }
}
