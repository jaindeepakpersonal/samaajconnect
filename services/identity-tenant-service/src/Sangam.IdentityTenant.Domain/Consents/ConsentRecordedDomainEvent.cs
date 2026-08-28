using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Consents;

public sealed record ConsentRecordedDomainEvent(
    Guid ConsentRecordId,
    Guid TenantId,
    Guid UserId,
    string Purpose,
    string Action,
    string NoticeVersion,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.consent.recorded.v1";
}
