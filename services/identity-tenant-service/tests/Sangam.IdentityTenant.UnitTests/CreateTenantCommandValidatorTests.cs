using FluentAssertions;
using Sangam.IdentityTenant.Application.Tenants.Commands.CreateTenant;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class CreateTenantCommandValidatorTests
{
    private readonly CreateTenantCommandValidator _validator = new();

    private static CreateTenantCommand Command(
        string name = "Mumbai Samaaj",
        string slug = "mumbai-samaaj",
        string? domain = null,
        string? email = null) =>
        new(name, slug, domain, null, email, null);

    [Fact]
    public void Accepts_a_well_formed_command()
    {
        _validator.Validate(Command()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("ab")]              // too short for a DNS label rule of 3+
    [InlineData("-mumbai")]         // leading hyphen
    [InlineData("mumbai-")]         // trailing hyphen
    [InlineData("mum bai")]         // space
    [InlineData("mumbai_samaaj")]   // underscore is not valid in a hostname
    [InlineData("MUMBAI!")]         // punctuation
    public void Rejects_a_slug_that_could_not_be_a_subdomain(string slug)
    {
        var result = _validator.Validate(Command(slug: slug));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTenantCommand.Slug));
    }

    [Fact]
    public void Accepts_a_slug_that_differs_only_by_casing_because_the_domain_normalises_it()
    {
        _validator.Validate(Command(slug: "Mumbai-Samaaj")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("api")]
    [InlineData("WWW")]
    public void Rejects_slugs_reserved_by_the_platform(string slug)
    {
        _validator.Validate(Command(slug: slug)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_an_empty_name()
    {
        _validator.Validate(Command(name: "")).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing@tld")]
    [InlineData("two@@at.com")]
    public void Rejects_a_malformed_contact_email(string email)
    {
        _validator.Validate(Command(email: email)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Ignores_contact_email_rules_when_it_is_omitted()
    {
        _validator.Validate(Command(email: null)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("mumbai.example.com")]
    [InlineData("samaaj.co.in")]
    public void Accepts_a_valid_custom_domain(string domain)
    {
        _validator.Validate(Command(domain: domain)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("no-dot")]
    [InlineData("-leading.example.com")]
    [InlineData("trailing-.example.com")]
    public void Rejects_a_malformed_custom_domain(string domain)
    {
        _validator.Validate(Command(domain: domain)).IsValid.Should().BeFalse();
    }
}
