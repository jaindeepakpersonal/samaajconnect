using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// Consumed by member-family-service to create the initial MemberProfile, and
/// by audit-notification-service to send the verification message.
/// </summary>
public sealed record UserRegisteredDomainEvent(
    Guid UserId,
    Guid TenantId,
    string MobileOrEmail,
    string FullName,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.user.registered.v1";
}
