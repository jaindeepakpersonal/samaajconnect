using FluentValidation;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.SetTenantModules;

public sealed class SetTenantModulesCommandValidator : AbstractValidator<SetTenantModulesCommand>
{
    public SetTenantModulesCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();

        // An empty list is legitimate - a Samaaj running no optional module at
        // all - so this checks for null, not for emptiness.
        RuleFor(x => x.EnabledModules).NotNull();

        // The whole point of the catalogue: an unrecognised key is not a
        // module nobody uses, it is every route of that module answering 404
        // for this Samaaj with nothing logged anywhere. Naming the keys back
        // is what turns a support ticket into a corrected form field.
        RuleFor(x => x.EnabledModules)
            .Must(modules => ModuleCatalog.Unknown(modules).Count == 0)
            .WithMessage(command =>
                "Unknown module(s): "
                + string.Join(", ", ModuleCatalog.Unknown(command.EnabledModules))
                + ". Known modules are: "
                + string.Join(", ", ModuleCatalog.All.Select(m => m.Key))
                + ".")
            .When(x => x.EnabledModules is not null);
    }
}
