using FluentValidation;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Authorization;

namespace Sangam.IdentityTenant.Application.Users.Commands.AssignRole;

public sealed class AssignRoleCommandValidator : AbstractValidator<AssignRoleCommand>
{
    public AssignRoleCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();

        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(role => AuthorizationCatalog.FindRoleByName(role) is not null)
            .WithMessage("No role by that name exists.");

        // The list an admin may hand out is narrower than the list of roles.
        // See AuthorizationCatalog.AdminAssignableRoleIds for why each of the
        // others is missing - SuperAdmin especially, which no request may ever
        // grant.
        RuleFor(x => x.Role)
            .Must(role => AuthorizationCatalog.FindRoleByName(role) is { } found
                && AuthorizationCatalog.IsAdminAssignable(found.Id))
            .WithMessage(command =>
                $"The {command.Role} role is not one an administrator assigns. "
                + "Assignable roles are: "
                + string.Join(", ", AuthorizationCatalog.Roles
                    .Where(r => AuthorizationCatalog.IsAdminAssignable(r.Id))
                    .Select(r => r.Name))
                + ".")
            .When(x => AuthorizationCatalog.FindRoleByName(x.Role) is not null);

        // AreaAdmin's whole substance is the locality it is restricted to
        // (UserRole.LocalityId); granting it with none would be an
        // administrator restricted to nothing, which is not restricted at all.
        RuleFor(x => x.LocalityId)
            .Must(value => !string.IsNullOrWhiteSpace(value)
                && Guid.TryParse(value, out var id) && id != Guid.Empty)
            .WithMessage("A locality is required to grant the AreaAdmin role.")
            .When(x => x.Granted && string.Equals(x.Role, Roles.AreaAdmin, StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.LocalityId)
            .Must(string.IsNullOrWhiteSpace)
            .WithMessage("A locality only applies to granting the AreaAdmin role.")
            .When(x => !(x.Granted && string.Equals(x.Role, Roles.AreaAdmin, StringComparison.OrdinalIgnoreCase)));
    }
}
