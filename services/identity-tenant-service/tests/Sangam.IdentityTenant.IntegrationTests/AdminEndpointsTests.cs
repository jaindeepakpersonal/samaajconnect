using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Security;
using Sangam.IdentityTenant.Domain.Authorization;
using Sangam.IdentityTenant.Domain.Tenants;
using Sangam.IdentityTenant.Domain.Users;
using Xunit;

/// <summary>
/// The admin portal's backend surface: the tenant list, module toggles, the
/// role matrix, and inviting and re-roling administrators.
/// </summary>
namespace Sangam.IdentityTenant.IntegrationTests;

public sealed class AdminEndpointsTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private Guid _tenantId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        (_tenantId, _) = await factory.SeedActiveTenantAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient PlatformAdmin() =>
        factory.CreateClientWith(PermissionKeys.TenantManage, PermissionKeys.AdminUsersManage);

    /// <summary>A Samaaj Admin of the seeded Samaaj, as a real one's token looks.</summary>
    private HttpClient SamaajAdmin(Guid? userId = null) =>
        factory.CreateClientAs(
            userId ?? Guid.NewGuid(),
            _tenantId,
            [Roles.SamaajAdmin],
            [PermissionKeys.AdminUsersManage]);

    private HttpClient Member() =>
        factory.CreateClientAs(Guid.NewGuid(), _tenantId, [Roles.Member], []);

    // ---- The tenant list -------------------------------------------------

    [Fact]
    public async Task The_tenant_list_shows_every_Samaaj_in_every_status()
    {
        var admin = PlatformAdmin();

        var created = await admin.PostAsJsonAsync("/v1/identity/tenants", new
        {
            name = "Adinath Samaaj",
            slug = "adinath-samaaj",
            enabledModules = Array.Empty<string>(),
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var listed = await admin.GetFromJsonAsync<JsonElement>("/v1/identity/tenants");

        // The Inactive one is exactly the Samaaj an admin is looking for, so
        // the default view must not hide it the way the public directory does.
        listed.EnumerateArray().Should().HaveCount(2);
        listed.EnumerateArray()
            .Select(t => t.GetProperty("status").GetString())
            .Should().Contain(["Active", "Inactive"]);
    }

    [Fact]
    public async Task The_tenant_list_carries_the_contact_details_the_public_one_withholds()
    {
        var listed = await PlatformAdmin().GetFromJsonAsync<JsonElement>("/v1/identity/tenants");

        listed.EnumerateArray().First().TryGetProperty("contactEmail", out _).Should().BeTrue();
    }

    [Fact]
    public async Task The_tenant_list_can_be_narrowed_by_status_and_by_name()
    {
        var admin = PlatformAdmin();

        await admin.PostAsJsonAsync("/v1/identity/tenants", new
        {
            name = "Adinath Samaaj",
            slug = "adinath-samaaj",
            enabledModules = Array.Empty<string>(),
        });

        var active = await admin.GetFromJsonAsync<JsonElement>("/v1/identity/tenants?status=Active");
        active.EnumerateArray().Should().ContainSingle();

        var byName = await admin.GetFromJsonAsync<JsonElement>("/v1/identity/tenants?search=adinath");
        byName.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task A_nonsense_status_is_a_validation_error_not_an_empty_list()
    {
        // An empty list would read as "there are no such Samaaj", which is a
        // different and much more alarming answer than "that is not a status".
        var response = await PlatformAdmin().GetAsync("/v1/identity/tenants?status=Dormant");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_tenant_list_is_refused_to_a_Samaaj_Admin()
    {
        // Which Samaaj exist on the platform is not one Samaaj's business.
        (await SamaajAdmin().GetAsync("/v1/identity/tenants"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_tenant_list_is_refused_without_a_token()
    {
        (await factory.CreateClient().GetAsync("/v1/identity/tenants"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Module toggles --------------------------------------------------

    [Fact]
    public async Task The_module_catalogue_is_public_because_it_fills_a_form()
    {
        var modules = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/v1/identity/tenants/modules");

        modules.EnumerateArray().Should().HaveCount(ModuleCatalog.All.Count);
        modules.EnumerateArray().Select(m => m.GetProperty("key").GetString())
            .Should().Contain(ModuleCatalog.Pathshala);
    }

    [Fact]
    public async Task Switching_modules_replaces_the_whole_set()
    {
        var response = await PlatformAdmin().PutAsJsonAsync(
            $"/v1/identity/tenants/{_tenantId}/modules",
            new { enabledModules = new[] { ModuleCatalog.Pathshala, ModuleCatalog.Boli } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("enabledModules").EnumerateArray()
            .Select(m => m.GetString())
            .Should().BeEquivalentTo([ModuleCatalog.Pathshala, ModuleCatalog.Boli]);
    }

    [Fact]
    public async Task Switching_modules_is_announced_so_the_change_reaches_the_audit_log()
    {
        // The gateway does not consume this - it re-reads the Samaaj when its
        // own 60-second cache expires, so a module change takes effect within a
        // minute with no consumer to keep in step. The event exists because
        // switching a module off makes a whole area of the platform answer 404
        // for everyone in that Samaaj, which is a decision worth recording.
        await PlatformAdmin().PutAsJsonAsync(
            $"/v1/identity/tenants/{_tenantId}/modules",
            new { enabledModules = new[] { ModuleCatalog.Boli } });

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "identity.tenant.modules-changed.v1")
                .ToListAsync());

        outbox.Should().ContainSingle();
    }

    [Fact]
    public async Task A_mistyped_module_is_refused_rather_than_stored()
    {
        // Stored, it would 404 every route of that module for this Samaaj with
        // nothing anywhere to say why.
        var response = await PlatformAdmin().PutAsJsonAsync(
            $"/v1/identity/tenants/{_tenantId}/modules",
            new { enabledModules = new[] { "pathshaala" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var persisted = await factory.WithDbContextAsync(db =>
            db.Tenants.AsNoTracking().SingleAsync(t => t.Id == _tenantId));

        persisted.EnabledModules.Should().NotContain("pathshaala");
    }

    [Fact]
    public async Task A_Samaaj_Admin_cannot_decide_which_modules_their_Samaaj_runs()
    {
        // It is what the gateway routes on, so it is a platform decision.
        var response = await SamaajAdmin().PutAsJsonAsync(
            $"/v1/identity/tenants/{_tenantId}/modules",
            new { enabledModules = new[] { ModuleCatalog.Boli } });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- The role matrix -------------------------------------------------

    [Fact]
    public async Task The_role_matrix_is_readable_by_anyone_signed_in()
    {
        var matrix = await Member().GetFromJsonAsync<JsonElement>("/v1/identity/roles");

        matrix.GetProperty("roles").EnumerateArray().Should().NotBeEmpty();
        matrix.GetProperty("permissions").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_role_matrix_says_it_is_not_editable()
    {
        var matrix = await Member().GetFromJsonAsync<JsonElement>("/v1/identity/roles");

        matrix.GetProperty("editable").GetBoolean().Should().BeFalse();
    }

    // ---- Inviting an administrator ---------------------------------------

    private Task<HttpResponseMessage> InviteAsync(
        HttpClient client,
        string identifier = "rajesh@example.com",
        string[]? roles = null) =>
        client.PostAsJsonAsync("/v1/identity/admins", new
        {
            fullName = "Rajesh Jain",
            mobileOrEmail = identifier,
            roles = roles ?? [Roles.SamaajAdmin],
        });

    [Fact]
    public async Task Inviting_an_admin_creates_an_unsignable_account_and_a_one_time_code()
    {
        var response = await InviteAsync(SamaajAdmin());

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.GetProperty("activationCode").GetString();

        code.Should().NotBeNullOrWhiteSpace();

        var invited = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .Include(u => u.ActivationCode)
                .SingleAsync(u => u.MobileOrEmail == "rajesh@example.com"));

        invited.Status.Should().Be(UserStatus.PendingActivation);
        invited.PasswordHash.Should().BeEmpty();

        // Stored as a hash. A database copy is not a working invitation.
        invited.ActivationCode!.Hash.Should().NotBe(code);
    }

    [Fact]
    public async Task An_invited_admin_holds_the_role_before_they_ever_sign_in()
    {
        await InviteAsync(SamaajAdmin());

        var invited = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .Include(u => u.Roles)
                .SingleAsync(u => u.MobileOrEmail == "rajesh@example.com"));

        invited.HasRole(AuthorizationCatalog.RoleIds.SamaajAdmin).Should().BeTrue();

        // Everyone with a login is a member of their Samaaj first.
        invited.HasRole(AuthorizationCatalog.RoleIds.Member).Should().BeTrue();
    }

    [Fact]
    public async Task An_invitation_lands_in_the_inviting_admin_s_own_Samaaj()
    {
        await InviteAsync(SamaajAdmin());

        var invited = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(u => u.MobileOrEmail == "rajesh@example.com"));

        invited.TenantId.Should().Be(_tenantId);
    }

    [Fact]
    public async Task An_identifier_already_on_the_platform_is_refused_without_saying_where()
    {
        await InviteAsync(SamaajAdmin());

        var second = await InviteAsync(SamaajAdmin());

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await second.Content.ReadAsStringAsync();

        // Naming the Samaaj would confirm an identifier exists on the platform
        // to anyone holding an admin account anywhere.
        body.Should().NotContain("mumbai-samaaj");
    }

    [Fact]
    public async Task Inviting_someone_into_SuperAdmin_is_refused()
    {
        (await InviteAsync(SamaajAdmin(), roles: [Roles.SuperAdmin]))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_plain_member_cannot_invite_an_administrator()
    {
        (await InviteAsync(Member())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Super_Admin_with_no_Samaaj_selected_is_told_to_pick_one()
    {
        // Their token carries no tenant. Guessing one would be worse than
        // saying so - it would create an administrator somewhere unintended.
        var response = await InviteAsync(
            factory.CreateClientWith(PermissionKeys.AdminUsersManage));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Granting and revoking -------------------------------------------

    private async Task<Guid> InvitedAdminIdAsync()
    {
        var response = await InviteAsync(SamaajAdmin(), roles: [Roles.ContentModerator]);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return body.GetProperty("userId").GetGuid();
    }

    [Fact]
    public async Task A_role_can_be_granted_and_taken_away_again()
    {
        var userId = await InvitedAdminIdAsync();
        var admin = SamaajAdmin();

        var granted = await admin.PutAsJsonAsync(
            $"/v1/identity/admins/{userId}/roles/{Roles.BoliManager}", new { granted = true });

        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await granted.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("changed").GetBoolean().Should().BeTrue();

        var revoked = await admin.PutAsJsonAsync(
            $"/v1/identity/admins/{userId}/roles/{Roles.BoliManager}", new { granted = false });

        revoked.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .Include(u => u.Roles).SingleAsync(u => u.Id == userId));

        user.HasRole(AuthorizationCatalog.RoleIds.BoliManager).Should().BeFalse();
    }

    [Fact]
    public async Task Granting_the_same_role_twice_reports_that_nothing_changed()
    {
        var userId = await InvitedAdminIdAsync();
        var admin = SamaajAdmin();
        var url = $"/v1/identity/admins/{userId}/roles/{Roles.BoliManager}";

        await admin.PutAsJsonAsync(url, new { granted = true });

        var again = await admin.PutAsJsonAsync(url, new { granted = true });

        (await again.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("changed").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Granting_announces_who_granted_what_to_whom()
    {
        var userId = await InvitedAdminIdAsync();

        await SamaajAdmin().PutAsJsonAsync(
            $"/v1/identity/admins/{userId}/roles/{Roles.BoliManager}", new { granted = true });

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "identity.user.role-granted.v1")
                .ToListAsync());

        outbox.Should().ContainSingle();
    }

    [Fact]
    public async Task Nobody_can_grant_SuperAdmin_through_this_endpoint()
    {
        var userId = await InvitedAdminIdAsync();

        var response = await PlatformAdmin().PutAsJsonAsync(
            $"/v1/identity/admins/{userId}/roles/{Roles.SuperAdmin}", new { granted = true });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_admin_cannot_remove_their_own_Samaaj_Admin_role()
    {
        // In a Samaaj with one administrator this locks everybody out. Another
        // admin can still do it, which makes it take two people.
        var self = Guid.NewGuid();
        var admin = SamaajAdmin(self);

        await admin.PostAsJsonAsync("/v1/identity/admins", new
        {
            fullName = "Self",
            mobileOrEmail = "self@example.com",
            roles = new[] { Roles.SamaajAdmin },
        });

        // Give the acting admin a real account holding the role.
        var actor = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(u => u.MobileOrEmail == "self@example.com"));

        var actingAsThemselves = SamaajAdmin(actor.Id);

        var response = await actingAsThemselves.PutAsJsonAsync(
            $"/v1/identity/admins/{actor.Id}/roles/{Roles.SamaajAdmin}", new { granted = false });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_role_cannot_be_granted_to_an_account_in_another_Samaaj()
    {
        // The IDOR guard. GetByIdAsync is tenant-filtered, and the handler
        // re-checks anyway, because this write hands out authority.
        var other = await factory.SeedActiveTenantAsync("adinath-samaaj");

        var elsewhere = factory.CreateClientAs(
            Guid.NewGuid(), other.Id, [Roles.SamaajAdmin], [PermissionKeys.AdminUsersManage]);

        await InviteAsync(elsewhere, "neha@example.com", [Roles.ContentModerator]);

        var target = await factory.WithDbContextAsync(db =>
            db.Users.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(u => u.MobileOrEmail == "neha@example.com"));

        var response = await SamaajAdmin().PutAsJsonAsync(
            $"/v1/identity/admins/{target.Id}/roles/{Roles.BoliManager}", new { granted = true });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- The admin list --------------------------------------------------

    [Fact]
    public async Task The_admin_list_shows_this_Samaaj_s_administrators_with_their_roles()
    {
        await InviteAsync(SamaajAdmin(), roles: [Roles.ContentModerator]);

        var listed = await SamaajAdmin().GetFromJsonAsync<JsonElement>("/v1/identity/admins");

        var entry = listed.EnumerateArray().Should().ContainSingle().Subject;

        entry.GetProperty("fullName").GetString().Should().Be("Rajesh Jain");
        entry.GetProperty("roles").EnumerateArray()
            .Select(r => r.GetString())
            .Should().Contain(Roles.ContentModerator);
    }

    [Fact]
    public async Task The_admin_list_does_not_cross_Samaaj()
    {
        var other = await factory.SeedActiveTenantAsync("adinath-samaaj");

        var elsewhere = factory.CreateClientAs(
            Guid.NewGuid(), other.Id, [Roles.SamaajAdmin], [PermissionKeys.AdminUsersManage]);

        await InviteAsync(elsewhere, "neha@example.com", [Roles.ContentModerator]);

        var listed = await SamaajAdmin().GetFromJsonAsync<JsonElement>("/v1/identity/admins");

        listed.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task The_admin_list_omits_ordinary_members()
    {
        // Everyone has the Member role. A list that included them would be the
        // member directory under another name.
        await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = "mumbai-samaaj",
            fullName = "Ravi Shah",
            mobileOrEmail = "ravi@example.com",
            password = "a-long-enough-password",
            consentedPurposes = new[] { "Membership" },
            noticeVersion = await NoticeVersionAsync(),
        });

        var listed = await SamaajAdmin().GetFromJsonAsync<JsonElement>("/v1/identity/admins");

        listed.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task The_admin_list_is_refused_to_a_plain_member()
    {
        (await Member().GetAsync("/v1/identity/admins"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<string> NoticeVersionAsync()
    {
        var notice = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/v1/identity/consent-notice");

        return notice.GetProperty("version").GetString()!;
    }
}
