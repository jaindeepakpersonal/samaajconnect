using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A password was reset by redeeming an anonymous code, rather than changed
/// by an authenticated member - see <see cref="PasswordChangedDomainEvent"/>
/// for that one. Two events rather than one with a discriminator, matching
/// this platform's own convention (<see cref="UserActivatedFromChildDomainEvent"/>
/// versus <see cref="UserRegisteredDomainEvent"/> is the same shape). Carries
/// the id and nothing else, the same reason <see cref="UserErasedDomainEvent"/>
/// does.
/// </summary>
public sealed record PasswordResetDomainEvent(
    Guid UserId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.password-reset.completed.v1";
}
