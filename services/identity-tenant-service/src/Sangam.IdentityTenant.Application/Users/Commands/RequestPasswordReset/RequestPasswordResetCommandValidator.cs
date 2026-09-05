using FluentValidation;

namespace Sangam.IdentityTenant.Application.Users.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
{
    public RequestPasswordResetCommandValidator()
    {
        RuleFor(x => x.MobileOrEmail).NotEmpty().MaximumLength(320);
    }
}
