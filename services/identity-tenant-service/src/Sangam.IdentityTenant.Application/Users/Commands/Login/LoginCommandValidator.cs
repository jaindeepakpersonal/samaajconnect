using FluentValidation;

namespace Sangam.IdentityTenant.Application.Users.Commands.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        // Presence and length only. Validating the *shape* of a login
        // identifier would tell an attacker which of their guesses are even
        // worth trying, and would lock out members whose identifier predates a
        // later tightening of the format rules.
        RuleFor(x => x.MobileOrEmail)
            .NotEmpty()
            .MaximumLength(320);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MaximumLength(256);
    }
}
