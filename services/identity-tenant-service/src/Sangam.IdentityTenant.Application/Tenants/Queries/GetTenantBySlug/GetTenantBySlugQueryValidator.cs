using FluentValidation;

namespace Sangam.IdentityTenant.Application.Tenants.Queries.GetTenantBySlug;

public sealed class GetTenantBySlugQueryValidator : AbstractValidator<GetTenantBySlugQuery>
{
    public GetTenantBySlugQueryValidator()
    {
        RuleFor(x => x.Slug)
            .NotEmpty()
            .MaximumLength(63);
    }
}
