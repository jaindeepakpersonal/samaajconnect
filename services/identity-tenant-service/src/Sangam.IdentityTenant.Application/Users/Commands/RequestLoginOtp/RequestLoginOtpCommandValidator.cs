using FluentValidation;

namespace Sangam.IdentityTenant.Application.Users.Commands.RequestLoginOtp;

public sealed class RequestLoginOtpCommandValidator : AbstractValidator<RequestLoginOtpCommand>
{
    public RequestLoginOtpCommandValidator()
    {
        RuleFor(x => x.MobileOrEmail).NotEmpty().MaximumLength(320);
    }
}
