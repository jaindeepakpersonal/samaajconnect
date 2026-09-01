using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Authorization;

/// <summary>
/// One Samaaj's departure from the platform's default role matrix.
/// </summary>
/// <remarks>
/// <b>An override, not a replacement.</b> `AuthorizationCatalog` stays the
/// default grant for every Samaaj; this table records only where one has
/// decided differently, as a row saying "for this Samaaj, this role does — or
/// does not — carry this permission". A Samaaj that has changed nothing has no
/// rows here and behaves exactly as it did before the matrix became editable.
///
/// That shape is what makes the feature safe to add to a running platform. The
/// alternative — copying the whole matrix per Samaaj on creation — would freeze
/// each Samaaj at the defaults of the day it was created, so a permission added
/// to a role later would reach nobody, silently, and the first symptom would be
/// a feature that works for new Samaaj and not old ones.
///
/// <b>What this does not change is what a command requires.</b> Every request
/// type declares its `[RequiresPermission]` in source, compiled in, and that
/// stays true: this table decides who *carries* a permission, not what a
/// command *needs*. Those are the two halves of role-based access control and
/// only one of them is a runtime decision. `ListRolesQuery` used to say an
/// editable matrix would put the answer to "who can approve a conversion?" half
/// in source control and half in a table; it does not, because the two halves
/// answer different questions.
/// </remarks>
public sealed class RolePermissionOverride : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    /// <summary>True adds a permission the default does not carry; false removes one it does.</summary>
    public bool Granted { get; private set; }

    public Guid ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    private RolePermissionOverride() { }   // EF Core

    /// <summary>
    /// Records a Samaaj departing from the default for the first time.
    /// </summary>
    /// <remarks>
    /// <paramref name="permissionKey"/> is taken only to put on the event, which
    /// reads better in an audit log than a bare id. It is not stored: the key is
    /// the catalogue's to change, and a copy here would be one that could drift.
    /// </remarks>
    public static RolePermissionOverride Create(
        Guid tenantId,
        Guid roleId,
        Guid permissionId,
        string permissionKey,
        bool granted,
        bool previouslyGranted,
        Guid changedBy,
        DateTimeOffset now)
    {
        var entry = new RolePermissionOverride
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RoleId = roleId,
            PermissionId = permissionId,
            Granted = granted,
            ChangedBy = changedBy,
            ChangedAt = now,
        };

        entry.Raise(new RoleMatrixChangedDomainEvent(
            tenantId, roleId, permissionKey, granted, previouslyGranted, changedBy, now));

        return entry;
    }

    /// <summary>Re-points an existing override, rather than stacking rows.</summary>
    public void Set(string permissionKey, bool granted, Guid changedBy, DateTimeOffset now)
    {
        var previouslyGranted = Granted;

        Granted = granted;
        ChangedBy = changedBy;
        ChangedAt = now;

        Raise(new RoleMatrixChangedDomainEvent(
            TenantId, RoleId, permissionKey, granted, previouslyGranted, changedBy, now));
    }

    /// <summary>
    /// Announces that this Samaaj is going back to the platform default.
    /// </summary>
    /// <remarks>
    /// The caller then deletes the row. Raising here rather than in the handler
    /// keeps every event this entity produces on the entity, which is what makes
    /// the outbox drain pick it up: `SaveChanges` reads domain events off tracked
    /// aggregates, and an event constructed in a handler is not on one.
    /// </remarks>
    public void ReturnToDefault(string permissionKey, bool defaultGrant, Guid changedBy, DateTimeOffset now) =>
        Raise(new RoleMatrixChangedDomainEvent(
            TenantId, RoleId, permissionKey, defaultGrant, Granted, changedBy, now));
}

/// <summary>
/// What a Samaaj may and may not change about its own matrix.
/// </summary>
/// <remarks>
/// `ListRolesQuery` named three things an editable matrix needs before it could
/// exist: per-tenant definitions, an audit trail, and "a floor of permissions no
/// edit may remove or the platform locks itself out". This is the floor.
/// </remarks>
public static class MatrixEditing
{
    /// <summary>
    /// Roles a Samaaj may not touch at all.
    /// </summary>
    /// <remarks>
    /// <b>SuperAdmin</b>, because it is platform administration rather than
    /// Samaaj administration. A Samaaj Admin able to edit it could grant the
    /// platform role a permission, or take one away from the only account that
    /// can put things right — and neither is theirs to decide. It is also the
    /// role that has to remain able to repair a Samaaj that has locked itself
    /// out, which it cannot do if a Samaaj can disarm it.
    /// </remarks>
    public static bool IsEditable(Guid roleId) => roleId != AuthorizationCatalog.RoleIds.SuperAdmin;

    /// <summary>
    /// Grants that cannot be revoked, whatever else a Samaaj changes.
    /// </summary>
    /// <remarks>
    /// Exactly one pair, and it is the lock-out floor: a Samaaj Admin must keep
    /// <c>Roles.Manage</c>. Without it the screen that edits the matrix refuses
    /// the very administrator who just used it, and the Samaaj cannot undo its
    /// own change — the one mistake this feature could make that a Samaaj could
    /// not recover from on its own.
    ///
    /// It is a floor rather than a warning because a warning is something
    /// somebody clicks past at the end of a long afternoon.
    /// </remarks>
    public static bool IsProtected(Guid roleId, Guid permissionId) =>
        roleId == AuthorizationCatalog.RoleIds.SamaajAdmin
        && permissionId == AuthorizationCatalog.PermissionIds.RolesManage;
}
