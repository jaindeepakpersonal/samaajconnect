using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

public sealed record UserLoggedInDomainEvent(
    Guid UserId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.user.logged-in.v1";
}
