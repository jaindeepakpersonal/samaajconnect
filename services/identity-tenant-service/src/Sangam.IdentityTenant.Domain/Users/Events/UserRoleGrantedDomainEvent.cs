using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// Someone was given a role.
/// </summary>
/// <remarks>
/// Published so the audit log records who granted what to whom, which is the
/// question asked first when an account turns out to have been able to do
/// something it should not have. Ids only - no name, no contact - because
/// audit-notification-service stores payloads verbatim in an append-only table.
/// </remarks>
public sealed record UserRoleGrantedDomainEvent(
    Guid UserId,
    Guid TenantId,
    Guid RoleId,
    Guid? TenantScope,
    Guid GrantedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.user.role-granted.v1";
}
