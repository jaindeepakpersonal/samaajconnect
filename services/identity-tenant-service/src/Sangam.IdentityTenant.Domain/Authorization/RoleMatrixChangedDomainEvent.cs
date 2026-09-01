using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Authorization;

/// <summary>
/// A Samaaj changed what one of its roles may do.
/// </summary>
/// <remarks>
/// The audit trail `ListRolesQuery` named as a precondition for an editable
/// matrix. It is the weightiest change an administrator can make on this
/// platform - not "this person may do X" but "everyone who ever holds this role
/// may do X" - so it is recorded with who made it and what it was before.
///
/// Ids and the permission key, no names: audit-notification-service records
/// every payload verbatim into an append-only table, and none of this is about
/// a person beyond the actor.
/// </remarks>
public sealed record RoleMatrixChangedDomainEvent(
    Guid TenantId,
    Guid RoleId,
    string PermissionKey,
    bool Granted,
    bool PreviouslyGranted,
    Guid ChangedBy,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.role-matrix.changed.v1";
}
