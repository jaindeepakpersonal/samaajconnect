using System.Text.RegularExpressions;
using FluentValidation;

namespace Sangam.IdentityTenant.Application.Users.Commands.RegisterMember;

public sealed partial class RegisterMemberCommandValidator : AbstractValidator<RegisterMemberCommand>
{
    /// <summary>An email address, or an Indian mobile number with or without +91.</summary>
    [GeneratedRegex(@"^([^@\s]+@[^@\s]+\.[^@\s]+|(\+91)?[6-9]\d{9})$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    public RegisterMemberCommandValidator()
    {
        RuleFor(x => x.TenantSlug)
            .NotEmpty()
            .MaximumLength(63);

        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.MobileOrEmail)
            .NotEmpty()
            .MaximumLength(320)
            .Must(value => IdentifierPattern().IsMatch(value.Trim()))
            .WithMessage("Enter a valid email address or 10-digit mobile number.");

        // Length carries most of the strength here. A composition rule (one
        // digit, one symbol) mostly teaches people to write Password1! - length
        // plus the lockout in User is the more honest defence.
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10)
            .WithMessage("Password must be at least 10 characters.")
            .MaximumLength(256);
    }
}
