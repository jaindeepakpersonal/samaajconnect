using System.Text.RegularExpressions;
using FluentValidation;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Commands.CreateTenant;

public sealed partial class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    // A slug becomes a subdomain label, so it is bound by DNS rules: lowercase
    // alphanumerics and interior hyphens, 3-63 characters.
    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^(?!-)[a-z0-9-]{1,63}(?<!-)(\.(?!-)[a-z0-9-]{1,63}(?<!-))+$", RegexOptions.CultureInvariant)]
    private static partial Regex DomainPattern();

    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(x => x.Slug)
            .NotEmpty()
            .MaximumLength(63)
            .Must(slug => SlugPattern().IsMatch(slug.Trim().ToLowerInvariant()))
            .WithMessage(
                "Slug must be 3-63 characters, lowercase letters, digits or hyphens, "
                + "and may not start or end with a hyphen.")
            .Must(slug => !ReservedSlugs.Contains(slug.Trim().ToLowerInvariant()))
            .WithMessage("This slug is reserved by the platform.");

        RuleFor(x => x.Domain)
            .MaximumLength(253)
            .Must(domain => DomainPattern().IsMatch(domain!.Trim().ToLowerInvariant()))
            .WithMessage("Domain must be a valid hostname.")
            .When(x => !string.IsNullOrWhiteSpace(x.Domain));

        RuleFor(x => x.ContactPerson)
            .MaximumLength(200);

        RuleFor(x => x.ContactEmail)
            .MaximumLength(320)
            .Must(email => EmailPattern().IsMatch(email!.Trim()))
            .WithMessage("Contact email must be a valid email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));

        // Same closed list as SetTenantModulesCommand. A Samaaj created with a
        // mistyped module key would have every route of that module answer 404
        // from the day it opened, with nothing logged anywhere to say why.
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

    /// <summary>
    /// Subdomains the platform itself uses. Handing one of these to a Samaaj
    /// would let its tenant subdomain shadow an infrastructure hostname.
    /// </summary>
    private static readonly HashSet<string> ReservedSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "api", "www", "app", "gateway", "static", "assets",
        "mail", "smtp", "ftp", "cdn", "status", "docs", "support",
    };
}
