using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants;

internal static class TenantMappings
{
    /// <summary>
    /// Where a client fetches this Samaaj logo, or null when there is none.
    /// </summary>
    /// <remarks>
    /// The wire field is still called <c>logoUrl</c> and still holds a string a
    /// client puts in an <c>img src</c>. What changed is who it points at: it
    /// used to be whatever host somebody typed - except that nothing could ever
    /// type one, because no command took a logo - and is now this platform.
    ///
    /// Relative, so this service does not have to be told a public hostname it
    /// has never needed. Both apps are same-origin with the gateway.
    /// </remarks>
    private static string? LogoPath(Tenant tenant) =>
        tenant.LogoImageId is null ? null : "/v1/identity/tenants/" + tenant.Id + "/logo";

    /// <summary>
    /// Where a member fetches this Samaaj's banner, or null when there is none.
    /// Members only, unlike the logo - see <c>GetTenantBannerQuery</c>. The id
    /// is in the path so a replaced banner is a new URL and no cache serves the
    /// old one.
    /// </summary>
    private static string? BannerPath(Tenant tenant) =>
        tenant.BannerImageId is not { } bannerId
            ? null
            : "/v1/identity/tenants/" + tenant.Id + "/banner?v=" + bannerId.ToString("N")[..8];

    public static TenantResponse ToResponse(this Tenant tenant) => new(
        tenant.Id,
        tenant.Name,
        tenant.Slug,
        tenant.Domain,
        LogoPath(tenant),
        BannerPath(tenant),
        tenant.ContactPerson,
        tenant.ContactEmail,
        tenant.Status.ToString(),
        tenant.EnabledModules,
        tenant.CreatedAt,
        tenant.GrievanceContactName is null
        && tenant.GrievanceContactEmail is null
        && tenant.GrievanceContactPhone is null
            ? null
            : new GrievanceContactResponse(
                tenant.GrievanceContactName,
                tenant.GrievanceContactEmail,
                tenant.GrievanceContactPhone));

    public static TenantSummaryResponse ToSummaryResponse(this Tenant tenant) => new(
        tenant.Id,
        tenant.Name,
        tenant.Slug,
        LogoPath(tenant),
        BannerPath(tenant),
        tenant.Status.ToString(),
        tenant.EnabledModules,
        tenant.GrievanceContactName is null
        && tenant.GrievanceContactEmail is null
        && tenant.GrievanceContactPhone is null
            ? null
            : new GrievanceContactResponse(
                tenant.GrievanceContactName,
                tenant.GrievanceContactEmail,
                tenant.GrievanceContactPhone));
}
