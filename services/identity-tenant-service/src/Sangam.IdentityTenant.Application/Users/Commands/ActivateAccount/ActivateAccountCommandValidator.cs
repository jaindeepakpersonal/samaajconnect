using FluentValidation;

namespace Sangam.IdentityTenant.Application.Users.Commands.ActivateAccount;

public sealed class ActivateAccountCommandValidator : AbstractValidator<ActivateAccountCommand>
{
    public ActivateAccountCommandValidator()
    {
        RuleFor(x => x.MobileOrEmail).NotEmpty().MaximumLength(320);

        // Length only, not shape: telling a guesser which of their attempts is
        // even well-formed narrows the search for them.
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);

        // Same rule as registration. A converted child's first password is a
        // real password, not a weaker one because an admin vouched for them.
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10)
            .WithMessage("Password must be at least 10 characters.")
            .MaximumLength(256);
    }
}
