using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Tenants;

public sealed record TenantStatusChangedDomainEvent(
    Guid TenantId,
    string PreviousStatus,
    string Status,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.tenant.status-changed.v1";
}
