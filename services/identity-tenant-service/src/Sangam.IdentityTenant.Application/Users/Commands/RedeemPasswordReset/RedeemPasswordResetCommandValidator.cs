using FluentValidation;

namespace Sangam.IdentityTenant.Application.Users.Commands.RedeemPasswordReset;

public sealed class RedeemPasswordResetCommandValidator : AbstractValidator<RedeemPasswordResetCommand>
{
    public RedeemPasswordResetCommandValidator()
    {
        RuleFor(x => x.MobileOrEmail).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(64);

        // Same rule as registration, activation and change-password - one
        // password policy, never four that can drift.
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(10).MaximumLength(256);
    }
}
