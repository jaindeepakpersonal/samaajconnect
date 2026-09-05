using FluentValidation;

namespace Sangam.IdentityTenant.Application.Users.Commands.SetUserSuspension;

public sealed class SetUserSuspensionCommandValidator : AbstractValidator<SetUserSuspensionCommand>
{
    public SetUserSuspensionCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        // Not required by shape - reinstating needs none - so this only checks
        // it is not sent empty when it is sent at all. Whether it was actually
        // required for this direction is the handler's question, once it knows
        // the target status; see SetUserSuspensionCommand.RequiresStepUp.
        RuleFor(x => x.Password)
            .NotEmpty()
            .When(x => x.Password is not null);
    }
}
