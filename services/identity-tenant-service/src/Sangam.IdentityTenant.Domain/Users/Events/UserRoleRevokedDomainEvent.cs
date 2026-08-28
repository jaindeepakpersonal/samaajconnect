using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>Someone had a role taken away. Ids only, as with the grant.</summary>
public sealed record UserRoleRevokedDomainEvent(
    Guid UserId,
    Guid TenantId,
    Guid RoleId,
    Guid? TenantScope,
    Guid RevokedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.user.role-revoked.v1";
}
