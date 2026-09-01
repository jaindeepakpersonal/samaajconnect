using FluentAssertions;
using Sangam.IdentityTenant.Domain.Authorization;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

/// <summary>
/// The floor `ListRolesQuery` named as a precondition for an editable matrix:
/// the permissions no edit may remove, or the platform locks itself out.
/// </summary>
public sealed class MatrixEditingTests
{
    [Fact]
    public void A_Samaaj_may_not_edit_SuperAdmin()
    {
        // It is platform administration, not Samaaj administration - and it is
        // the role that has to be able to repair a Samaaj that has locked
        // itself out, which it cannot do if a Samaaj can disarm it.
        MatrixEditing.IsEditable(AuthorizationCatalog.RoleIds.SuperAdmin).Should().BeFalse();
    }

    [Theory]
    [InlineData("a0000000-0000-0000-0000-000000000002")] // SamaajAdmin
    [InlineData("a0000000-0000-0000-0000-000000000003")] // Member
    [InlineData("a0000000-0000-0000-0000-000000000009")] // BoliManager
    public void Every_other_role_may_be_edited(string roleId)
    {
        MatrixEditing.IsEditable(Guid.Parse(roleId)).Should().BeTrue();
    }

    [Fact]
    public void A_Samaaj_administrator_cannot_be_stripped_of_the_ability_to_change_this()
    {
        // The one revocation a Samaaj could not undo for itself: without
        // Roles.Manage the screen that edits the matrix refuses the very
        // administrator who just used it.
        MatrixEditing.IsProtected(
            AuthorizationCatalog.RoleIds.SamaajAdmin,
            AuthorizationCatalog.PermissionIds.RolesManage).Should().BeTrue();
    }

    [Fact]
    public void Nothing_else_is_protected()
    {
        // The floor is deliberately one pair. Every additional protected cell
        // is a decision taken away from a Samaaj about its own community, and
        // this one is only here because of what it would cost to undo.
        MatrixEditing.IsProtected(
            AuthorizationCatalog.RoleIds.SamaajAdmin,
            AuthorizationCatalog.PermissionIds.BoliManage).Should().BeFalse();

        MatrixEditing.IsProtected(
            AuthorizationCatalog.RoleIds.Member,
            AuthorizationCatalog.PermissionIds.RolesManage).Should().BeFalse();
    }

    [Fact]
    public void The_platform_grants_Roles_Manage_to_somebody_who_can_actually_use_it()
    {
        // A permission carried only by a role nothing grants is a permission
        // nobody has - the trap SECURITY-CHECKLIST.md records three services
        // falling into. SamaajAdmin is granted, so this one is reachable.
        AuthorizationCatalog.RolePermissions.Should().Contain(rp =>
            rp.RoleId == AuthorizationCatalog.RoleIds.SamaajAdmin
            && rp.PermissionId == AuthorizationCatalog.PermissionIds.RolesManage);
    }
}

/// <summary>
/// How a Samaaj's overrides combine with the platform defaults.
/// </summary>
public sealed class RolePermissionOverrideTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Creating_an_override_announces_it()
    {
        // The audit trail ListRolesQuery named as a precondition. This is the
        // weightiest change an administrator can make - not "this person may do
        // X" but "everyone who holds this role may do X".
        var entry = RolePermissionOverride.Create(
            Tenant,
            AuthorizationCatalog.RoleIds.ContentModerator,
            AuthorizationCatalog.PermissionIds.BoliManage,
            "Boli.Manage",
            granted: true,
            previouslyGranted: false,
            Guid.NewGuid(),
            Now);

        entry.DomainEvents.Should().ContainSingle()
            .Which.Topic.Should().Be("identity.role-matrix.changed.v1");
    }

    [Fact]
    public void Re_pointing_an_override_carries_what_it_was_before()
    {
        var entry = RolePermissionOverride.Create(
            Tenant, Guid.NewGuid(), Guid.NewGuid(), "Boli.Manage",
            granted: true, previouslyGranted: false, Guid.NewGuid(), Now);

        entry.ClearDomainEvents();

        entry.Set("Boli.Manage", granted: false, Guid.NewGuid(), Now.AddMinutes(1));

        var announced = entry.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<RoleMatrixChangedDomainEvent>().Subject;

        announced.Granted.Should().BeFalse();
        announced.PreviouslyGranted.Should().BeTrue();
    }

    [Fact]
    public void Returning_to_the_default_is_announced_too()
    {
        // The row is about to be deleted, so the event has to be raised while
        // there is still an aggregate for SaveChanges to read it off.
        var entry = RolePermissionOverride.Create(
            Tenant, Guid.NewGuid(), Guid.NewGuid(), "Boli.Manage",
            granted: false, previouslyGranted: true, Guid.NewGuid(), Now);

        entry.ClearDomainEvents();

        entry.ReturnToDefault("Boli.Manage", defaultGrant: true, Guid.NewGuid(), Now);

        var announced = entry.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<RoleMatrixChangedDomainEvent>().Subject;

        announced.Granted.Should().BeTrue();
        announced.PreviouslyGranted.Should().BeFalse();
    }
}
