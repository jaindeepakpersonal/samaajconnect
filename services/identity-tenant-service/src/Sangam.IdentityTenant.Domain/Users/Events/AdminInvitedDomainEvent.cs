using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Users;

/// <summary>
/// A Samaaj Admin invited someone into an administrative role.
/// </summary>
/// <remarks>
/// Ids only - no name, no contact, no code. audit-notification-service records
/// payloads verbatim into an append-only table, and this event is about the
/// creation of privileged access, which is exactly the payload worth keeping
/// free of anything that would later have to be redacted.
///
/// Note that this does <i>not</i> create a member profile: the invited person
/// has no login yet. member-family-service acts on
/// identity.user.registered.v1, and that is published when the invitation is
/// redeemed, not when it is sent.
/// </remarks>
public sealed record AdminInvitedDomainEvent(
    Guid UserId,
    Guid TenantId,
    IReadOnlyCollection<Guid> RoleIds,
    Guid InvitedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.admin.invited.v1";
}
