using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// Tells the rest of the platform to erase this person. Carries the id and
/// nothing else: an event announcing an erasure must not itself be a copy of
/// what was erased, least of all in an append-only audit table.
/// </summary>
public sealed record UserErasedDomainEvent(
    Guid UserId,
    Guid TenantId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.user.erased.v1";
}
