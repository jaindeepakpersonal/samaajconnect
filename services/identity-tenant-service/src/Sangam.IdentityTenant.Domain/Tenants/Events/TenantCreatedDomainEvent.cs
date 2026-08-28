using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Tenants;

public sealed record TenantCreatedDomainEvent(
    Guid TenantId,
    string Name,
    string Slug,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.tenant.created.v1";
}
