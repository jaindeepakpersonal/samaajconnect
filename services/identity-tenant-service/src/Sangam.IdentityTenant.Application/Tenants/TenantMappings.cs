using Sangam.IdentityTenant.Domain.Tenants;

namespace Sangam.IdentityTenant.Application.Tenants;

internal static class TenantMappings
{
    public static TenantResponse ToResponse(this Tenant tenant) => new(
        tenant.Id,
        tenant.Name,
        tenant.Slug,
        tenant.Domain,
        tenant.LogoUrl,
        tenant.ContactPerson,
        tenant.ContactEmail,
        tenant.Status.ToString(),
        tenant.EnabledModules,
        tenant.CreatedAt);

    public static TenantSummaryResponse ToSummaryResponse(this Tenant tenant) => new(
        tenant.Id,
        tenant.Name,
        tenant.Slug,
        tenant.LogoUrl,
        tenant.Status.ToString(),
        tenant.EnabledModules);
}
