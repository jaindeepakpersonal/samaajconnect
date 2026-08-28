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
    }
}
