using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Media;

/// <summary>
/// A Samaaj's banner - to its own members, and to a Super Admin.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not the logo's rule.</b> The logo is anonymous because the
/// registration form needs it before anyone has an account, and it is a mark
/// that shows nobody. A banner is a photograph - a gathering, a temple, a
/// procession - and a photograph of a community usually has its members in it.
/// That is a member's photo's rule, not a letterhead's: served to the Samaaj it
/// belongs to, checked per request, and never to a shared cache.
/// </para>
/// <para>
/// Another Samaaj's banner is "no banner" rather than 403, so the answer does
/// not confirm which Samaaj have one.
/// </para>
/// </remarks>
[RequiresRoles(Roles.Member, Roles.SamaajAdmin, Roles.AreaAdmin, Roles.SuperAdmin)]
public sealed record GetTenantBannerQuery(Guid TenantId) : IQuery<TenantImageContent>;

public sealed class GetTenantBannerQueryValidator : AbstractValidator<GetTenantBannerQuery>
{
    public GetTenantBannerQueryValidator() => RuleFor(x => x.TenantId).NotEmpty();
}

public sealed class GetTenantBannerQueryHandler(
    ITenantRepository tenants,
    ITenantImageStore images,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IRequestHandler<GetTenantBannerQuery, Result<TenantImageContent>>
{
    public async Task<Result<TenantImageContent>> Handle(
        GetTenantBannerQuery query,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsInRole(Roles.SuperAdmin) && tenantContext.TenantId != query.TenantId)
        {
            return NoBanner();
        }

        var tenant = await tenants.GetByIdAsync(query.TenantId, cancellationToken);

        if (tenant?.BannerImageId is not { } bannerId
            || await images.GetAsync(bannerId, cancellationToken) is not { } banner)
        {
            return NoBanner();
        }

        return Result.Success(new TenantImageContent(
            banner.Bytes, banner.ContentType, banner.ContentHash, banner.UploadedAt));
    }

    private static Result<TenantImageContent> NoBanner() =>
        Result.Failure<TenantImageContent>(
            Error.NotFound("Tenant.NoBanner", "No banner for that Samaaj."));
}
