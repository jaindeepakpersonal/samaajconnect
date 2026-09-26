using System.Text.RegularExpressions;
using FluentValidation;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Authorization;

namespace Sangam.IdentityTenant.Application.Users.Commands.InviteAdmin;

public sealed partial class InviteAdminCommandValidator : AbstractValidator<InviteAdminCommand>
{
    /// <summary>
    /// The identifier rule from <c>RegisterMemberCommandValidator</c>, character
    /// for character. An invited admin and a self-registering member land in the
    /// same platform-unique column, so a value one accepts and the other rejects
    /// would be a login that exists and cannot be typed. If either pattern
    /// changes, the other has to change with it.
    /// </summary>
    [GeneratedRegex(@"^([^@\s]+@[^@\s]+\.[^@\s]+|(\+91)?[6-9]\d{9})$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    public InviteAdminCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);

        RuleFor(x => x.MobileOrEmail)
            .NotEmpty()
            .MaximumLength(320)
            .Must(value => IdentifierPattern().IsMatch(value.Trim()))
            .WithMessage("Enter a valid mobile number or email address.");

        // An invitation with no role is just an account nobody asked for.
        RuleFor(x => x.Roles).NotEmpty();

        RuleForEach(x => x.Roles)
            .Must(role => AuthorizationCatalog.FindRoleByName(role) is { } found
                && AuthorizationCatalog.IsAdminAssignable(found.Id))
            .WithMessage("{PropertyValue} is not a role an administrator assigns.")
            .When(x => x.Roles is not null);

        // AreaAdmin's whole substance is the locality it is restricted to
        // (UserRole.LocalityId), so an invitation naming it must supply one,
        // and never alongside another admin role - mixing would leave it
        // ambiguous which grant the locality belongs to.
        RuleFor(x => x.Roles)
            .Must(roles => roles.Count == 1)
            .WithMessage("AreaAdmin cannot be invited alongside another role.")
            .When(x => x.Roles is not null && x.Roles.Contains(Roles.AreaAdmin));

        RuleFor(x => x.LocalityId)
            .Must(value => !string.IsNullOrWhiteSpace(value)
                && Guid.TryParse(value, out var id) && id != Guid.Empty)
            .WithMessage("A locality is required to invite an Area Admin.")
            .When(x => x.Roles is not null && x.Roles.Contains(Roles.AreaAdmin));

        RuleFor(x => x.LocalityId)
            .Must(string.IsNullOrWhiteSpace)
            .WithMessage("A locality only applies to an Area Admin invitation.")
            .When(x => x.Roles is null || !x.Roles.Contains(Roles.AreaAdmin));
    }
}
