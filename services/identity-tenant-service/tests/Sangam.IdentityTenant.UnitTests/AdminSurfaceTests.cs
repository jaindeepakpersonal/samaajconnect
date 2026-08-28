using FluentAssertions;
using Sangam.IdentityTenant.Application.Authorization.Queries.ListRoles;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Application.Tenants.Commands.SetTenantModules;
using Sangam.IdentityTenant.Application.Users.Commands.AssignRole;
using Sangam.IdentityTenant.Application.Users.Commands.InviteAdmin;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

namespace Sangam.IdentityTenant.UnitTests;

public sealed class ModuleCatalogTests
{
    [Theory]
    [InlineData("pathshala")]
    [InlineData("Pathshala")]
    [InlineData("  PATHSHALA  ")]
    public void Any_spelling_of_a_known_key_resolves_to_the_catalogue_s_own(string input)
    {
        ModuleCatalog.Canonical(input).Should().Be(ModuleCatalog.Pathshala);
    }

    [Fact]
    public void A_key_the_catalogue_does_not_know_is_named_back()
    {
        // The whole point: a typo has to be reportable. Silently dropping it
        // produces a Samaaj whose Pathshala routes 404 with nothing to explain
        // why, which is indistinguishable from having switched the module off.
        ModuleCatalog.Unknown(["pathshala", "pathshaala"])
            .Should().ContainSingle().Which.Should().Be("pathshaala");
    }

    [Fact]
    public void Boli_is_the_one_module_off_by_default()
    {
        // Per the admin wireframe. A Samaaj that runs no auctions should not
        // have to notice the feature in order to turn it off.
        ModuleCatalog.DefaultKeys.Should().NotContain(ModuleCatalog.Boli);
        ModuleCatalog.DefaultKeys.Should().Contain(ModuleCatalog.Pathshala);
    }
}

public sealed class TenantModuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static Tenant Tenant() =>
        Domain.Tenants.Tenant.Create(
            "Mumbai Samaaj", "mumbai", null, null, null,
            [ModuleCatalog.Pathshala, ModuleCatalog.Community], Now);

    [Fact]
    public void Setting_the_same_set_again_changes_nothing_and_raises_nothing()
    {
        // Otherwise every save of an untouched toggle row would publish an
        // event and write an audit entry saying a decision was made.
        var tenant = Tenant();
        tenant.ClearDomainEvents();

        var changed = tenant.SetEnabledModules(
            [ModuleCatalog.Community, ModuleCatalog.Pathshala], Now);

        changed.Should().BeFalse();
        tenant.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void The_same_set_in_a_different_case_is_still_the_same_set()
    {
        var tenant = Tenant();
        tenant.ClearDomainEvents();

        tenant.SetEnabledModules(["PATHSHALA", "Community"], Now).Should().BeFalse();
    }

    [Fact]
    public void Switching_a_module_off_raises_an_event_carrying_both_sets()
    {
        var tenant = Tenant();
        tenant.ClearDomainEvents();

        tenant.SetEnabledModules([ModuleCatalog.Community], Now).Should().BeTrue();

        var raised = tenant.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<TenantModulesChangedDomainEvent>().Subject;

        // Both sets, so a consumer that missed a message can correct itself
        // from this one rather than replaying every change.
        raised.PreviousModules.Should().Contain(ModuleCatalog.Pathshala);
        raised.EnabledModules.Should().BeEquivalentTo([ModuleCatalog.Community]);
    }

    [Fact]
    public void An_empty_set_is_legitimate()
    {
        var tenant = Tenant();

        tenant.SetEnabledModules([], Now).Should().BeTrue();
        tenant.EnabledModules.Should().BeEmpty();
    }
}

public sealed class SetTenantModulesCommandValidatorTests
{
    private readonly SetTenantModulesCommandValidator _validator = new();

    [Fact]
    public void An_unknown_module_is_refused_and_the_known_ones_are_listed()
    {
        var result = _validator.Validate(
            new SetTenantModulesCommand(Guid.NewGuid(), ["pathshaala"]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.ErrorMessage.Should().Contain("pathshaala").And.Contain(ModuleCatalog.Pathshala);
    }

    [Fact]
    public void An_empty_set_passes_because_running_no_optional_module_is_allowed()
    {
        _validator.Validate(new SetTenantModulesCommand(Guid.NewGuid(), [])).IsValid
            .Should().BeTrue();
    }
}

public sealed class RoleMatrixTests
{
    private readonly ListRolesQueryHandler _handler = new();

    private RoleMatrixResponse Matrix() =>
        _handler.Handle(new ListRolesQuery(), CancellationToken.None).Result.Value;

    [Fact]
    public void The_matrix_reports_the_catalogue_the_pipeline_actually_checks()
    {
        var matrix = Matrix();

        matrix.Roles.Should().HaveCount(AuthorizationCatalog.Roles.Count);
        matrix.Permissions.Should().HaveCount(AuthorizationCatalog.Permissions.Count);
    }

    [Fact]
    public void A_Super_Admin_holds_every_permission_by_construction()
    {
        var superAdmin = Matrix().Roles.Single(r => r.Name == Roles.SuperAdmin);

        superAdmin.Permissions.Should().HaveCount(AuthorizationCatalog.Permissions.Count);
    }

    [Fact]
    public void The_matrix_says_it_is_not_editable_rather_than_letting_a_screen_assume()
    {
        var matrix = Matrix();

        matrix.Editable.Should().BeFalse();
        matrix.EditableNote.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SuperAdmin_is_never_a_role_an_administrator_can_hand_out()
    {
        // Its only route is the bootstrap on an empty database, so granting it
        // can never be one compromised admin account away.
        Matrix().Roles.Single(r => r.Name == Roles.SuperAdmin)
            .AssignableToAdmins.Should().BeFalse();
    }

    [Theory]
    [InlineData(Roles.Member)]
    [InlineData(Roles.FamilyHead)]
    [InlineData(Roles.PathshalaStudent)]
    public void Earned_roles_are_not_assignable_either(string role)
    {
        Matrix().Roles.Single(r => r.Name == role).AssignableToAdmins.Should().BeFalse();
    }

    [Fact]
    public void SamaajAdmin_is_assignable_because_that_is_the_point_of_the_screen()
    {
        Matrix().Roles.Single(r => r.Name == Roles.SamaajAdmin)
            .AssignableToAdmins.Should().BeTrue();
    }
}

public sealed class AssignRoleCommandValidatorTests
{
    private readonly AssignRoleCommandValidator _validator = new();

    private bool IsValid(string role) =>
        _validator.Validate(new AssignRoleCommand(Guid.NewGuid(), role, true)).IsValid;

    [Fact]
    public void SuperAdmin_cannot_be_granted_through_this_command()
    {
        IsValid(Roles.SuperAdmin).Should().BeFalse();
    }

    [Fact]
    public void Member_cannot_be_granted_because_it_is_earned_by_registering()
    {
        IsValid(Roles.Member).Should().BeFalse();
    }

    [Fact]
    public void A_role_nobody_has_heard_of_is_refused()
    {
        IsValid("Emperor").Should().BeFalse();
    }

    [Fact]
    public void ContentModerator_is_accepted()
    {
        IsValid(Roles.ContentModerator).Should().BeTrue();
    }
}

public sealed class InviteAdminCommandValidatorTests
{
    private readonly InviteAdminCommandValidator _validator = new();

    private bool IsValid(
        string name = "Rajesh Jain",
        string identifier = "rajesh@example.com",
        string[]? roles = null) =>
        _validator.Validate(
            new InviteAdminCommand(name, identifier, roles ?? [Roles.SamaajAdmin])).IsValid;

    [Fact]
    public void An_invitation_with_no_role_is_refused()
    {
        // It would create an account nobody asked for and nobody can use.
        IsValid(roles: []).Should().BeFalse();
    }

    [Fact]
    public void SuperAdmin_cannot_be_invited_into()
    {
        IsValid(roles: [Roles.SuperAdmin]).Should().BeFalse();
    }

    [Theory]
    [InlineData("rajesh@example.com")]
    [InlineData("9876543210")]
    [InlineData("+919876543210")]
    public void The_identifier_rule_matches_registration_s(string identifier)
    {
        // Invited admins and self-registering members land in the same
        // platform-unique column, so what one accepts the other must.
        IsValid(identifier: identifier).Should().BeTrue();
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("not-an-email")]
    public void And_rejects_what_registration_rejects(string identifier)
    {
        IsValid(identifier: identifier).Should().BeFalse();
    }
}

public sealed class UserRoleGrantTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    private static User Member() =>
        User.Register(TenantId, "ravi@example.com", "Ravi Shah", "hash",
            AuthorizationCatalog.RoleIds.Member, Now);

    [Fact]
    public void Granting_a_role_twice_is_a_no_op_the_second_time()
    {
        var user = Member();

        user.GrantRole(AuthorizationCatalog.RoleIds.SamaajAdmin, TenantId, ActorId, Now)
            .Should().BeTrue();

        user.GrantRole(AuthorizationCatalog.RoleIds.SamaajAdmin, TenantId, ActorId, Now)
            .Should().BeFalse();

        user.Roles.Count(r => r.RoleId == AuthorizationCatalog.RoleIds.SamaajAdmin)
            .Should().Be(1);
    }

    [Fact]
    public void Granting_announces_who_granted_what_to_whom()
    {
        var user = Member();
        user.ClearDomainEvents();

        user.GrantRole(AuthorizationCatalog.RoleIds.SamaajAdmin, TenantId, ActorId, Now);

        var raised = user.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<UserRoleGrantedDomainEvent>().Subject;

        raised.GrantedBy.Should().Be(ActorId);
        raised.RoleId.Should().Be(AuthorizationCatalog.RoleIds.SamaajAdmin);
    }

    [Fact]
    public void Revoking_a_role_the_user_never_held_is_a_no_op()
    {
        // Two admins revoking the same grant at once is normal, not an error.
        var user = Member();
        user.ClearDomainEvents();

        user.RevokeRole(AuthorizationCatalog.RoleIds.BoliManager, TenantId, ActorId, Now)
            .Should().BeFalse();

        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_grant_scoped_to_one_Samaaj_is_not_a_grant_in_another()
    {
        var user = Member();

        user.GrantRole(AuthorizationCatalog.RoleIds.SamaajAdmin, TenantId, ActorId, Now);

        user.RevokeRole(AuthorizationCatalog.RoleIds.SamaajAdmin, Guid.NewGuid(), ActorId, Now)
            .Should().BeFalse();

        user.HasRole(AuthorizationCatalog.RoleIds.SamaajAdmin).Should().BeTrue();
    }

    [Fact]
    public void An_invited_admin_is_a_member_first_and_cannot_sign_in_yet()
    {
        var invited = User.Invite(
            TenantId, "rajesh@example.com", "Rajesh Jain",
            AuthorizationCatalog.RoleIds.Member,
            [AuthorizationCatalog.RoleIds.SamaajAdmin],
            ActorId,
            Now);

        invited.Status.Should().Be(UserStatus.PendingActivation);
        invited.PasswordHash.Should().BeEmpty();
        invited.HasRole(AuthorizationCatalog.RoleIds.Member).Should().BeTrue();
        invited.HasRole(AuthorizationCatalog.RoleIds.SamaajAdmin).Should().BeTrue();
    }

    [Fact]
    public void Inviting_someone_into_Member_does_not_grant_it_twice()
    {
        var invited = User.Invite(
            TenantId, "rajesh@example.com", "Rajesh Jain",
            AuthorizationCatalog.RoleIds.Member,
            [AuthorizationCatalog.RoleIds.Member, AuthorizationCatalog.RoleIds.SamaajAdmin],
            ActorId,
            Now);

        invited.Roles.Count(r => r.RoleId == AuthorizationCatalog.RoleIds.Member).Should().Be(1);
    }
}
