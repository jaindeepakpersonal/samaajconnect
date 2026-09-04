using FluentValidation;
using MediatR;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Application.Common;
using Sangam.IdentityTenant.Application.Security;

namespace Sangam.IdentityTenant.Application.Media;

/// <summary>
/// A Samaaj's logo. Anonymous, deliberately.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the one image on the platform that is not authorization-checked
/// per request, and the reason is the registration form.</b> Somebody signing up
/// picks which Samaaj they belong to before they have an account, so
/// <c>ListRegisterableTenantsQuery</c> and <c>GetTenantBySlugQuery</c> are both
/// anonymous by necessity. A logo that required a token could not appear on the
/// one screen that most needs it.
/// </para>
/// <para>
/// What that costs is worth stating plainly rather than waving through. It
/// publishes an organisation's mark to anyone who has a Samaaj id — and the
/// directory those ids come from is itself anonymous and already publishes the
/// name and slug beside them, so a logo adds nothing a caller did not have. It
/// reveals nothing about any person, which is the difference from a member's
/// photo that makes this acceptable where that would not be.
/// </para>
/// <para>
/// So this endpoint is deliberately outside the tick that
/// <c>SECURITY-CHECKLIST.md</c> gives "file storage access is
/// authorization-checked per request", and that page says so. The size cap and
/// the format sniffing still apply: those govern what the platform will store
/// and serve, not who may see it.
/// </para>
/// </remarks>
[AllowAnonymousRequest]
public sealed record GetTenantLogoQuery(Guid TenantId) : IQuery<LogoContent>;

public sealed class GetTenantLogoQueryValidator : AbstractValidator<GetTenantLogoQuery>
{
    public GetTenantLogoQueryValidator() => RuleFor(x => x.TenantId).NotEmpty();
}

public sealed class GetTenantLogoQueryHandler(
    ITenantRepository tenants,
    ILogoStore logos)
    : IRequestHandler<GetTenantLogoQuery, Result<LogoContent>>
{
    public async Task<Result<LogoContent>> Handle(
        GetTenantLogoQuery query,
        CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetByIdAsync(query.TenantId, cancellationToken);

        // No such Samaaj and no logo answer the same way. The distinction is of
        // no use to the caller and the directory is where Samaaj are discovered,
        // not this endpoint.
        if (tenant?.LogoImageId is not { } logoId)
        {
            return Result.Failure<LogoContent>(
                Error.NotFound("Tenant.NoLogo", "No logo for that Samaaj."));
        }

        var logo = await logos.GetAsync(logoId, cancellationToken);

        if (logo is null)
        {
            return Result.Failure<LogoContent>(
                Error.NotFound("Tenant.NoLogo", "No logo for that Samaaj."));
        }

        return Result.Success(new LogoContent(
            logo.Bytes, logo.ContentType, logo.ContentHash, logo.UploadedAt));
    }
}

/// <summary>Bytes ready to be written to a response, with what a cache needs.</summary>
public sealed record LogoContent(
    byte[] Bytes,
    string ContentType,
    string ETag,
    DateTimeOffset UploadedAt);
