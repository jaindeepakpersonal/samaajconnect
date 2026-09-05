using FluentValidation;

namespace Sangam.IdentityTenant.Application.Users.Commands.LoginWithOtp;

public sealed class LoginWithOtpCommandValidator : AbstractValidator<LoginWithOtpCommand>
{
    public LoginWithOtpCommandValidator()
    {
        RuleFor(x => x.MobileOrEmail).NotEmpty().MaximumLength(320);

        // Presence only, matching LoginCommandValidator's own reasoning:
        // validating shape would tell an attacker which guesses are worth
        // trying.
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);
    }
}
