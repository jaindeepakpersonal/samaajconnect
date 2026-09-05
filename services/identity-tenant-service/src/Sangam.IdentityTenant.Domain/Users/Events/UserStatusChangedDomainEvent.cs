using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// An account was suspended or reinstated by an administrator.
/// </summary>
/// <remarks>
/// Named and shaped after <c>TenantStatusChangedDomainEvent</c> deliberately:
/// the two are the same kind of fact ("something that can serve traffic
/// stopped or started again"), one Samaaj-wide and one account-wide, and there
/// is no reason for their audit rows to look different.
/// </remarks>
public sealed record UserStatusChangedDomainEvent(
    Guid UserId,
    Guid TenantId,
    string PreviousStatus,
    string Status,
    Guid ChangedByUserId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.user.status-changed.v1";
}
