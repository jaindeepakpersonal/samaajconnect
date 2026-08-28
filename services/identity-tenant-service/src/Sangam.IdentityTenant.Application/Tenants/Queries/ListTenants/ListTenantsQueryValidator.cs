using FluentValidation;
using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.ListTenants;

public sealed class ListTenantsQueryValidator : AbstractValidator<ListTenantsQuery>
{
    public ListTenantsQueryValidator()
    {
        RuleFor(x => x.Status)
            .Must(status => Enum.TryParse<TenantStatus>(status, ignoreCase: true, out _))
            .WithMessage("Status must be one of Active, Inactive or Archived.")
            .When(x => !string.IsNullOrWhiteSpace(x.Status));

        RuleFor(x => x.Search).MaximumLength(200);
    }
}
