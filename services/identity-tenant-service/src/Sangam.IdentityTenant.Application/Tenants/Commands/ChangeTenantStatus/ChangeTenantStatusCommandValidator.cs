using FluentValidation;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.ChangeTenantStatus;

public sealed class ChangeTenantStatusCommandValidator : AbstractValidator<ChangeTenantStatusCommand>
{
    public ChangeTenantStatusCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(status => Enum.TryParse<TenantStatus>(status, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<TenantStatus>())}.");

        // Length only. Whether the password is *right* is the handler's
        // question, and answering it here would leak the difference between a
        // missing field and a wrong password into a 400 - the validator reports
        // per-field messages, which is exactly the wrong shape for a credential.
        RuleFor(x => x.Password)
            .MaximumLength(256);
    }
}
