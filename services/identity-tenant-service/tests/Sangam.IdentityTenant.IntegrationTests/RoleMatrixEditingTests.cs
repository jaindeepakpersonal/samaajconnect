using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Domain.Authorization;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Editing the role matrix, end to end.
/// </summary>
/// <remarks>
/// The assertion that matters is not that the screen shows a change — it is
/// that <c>GetAuthorizationAsync</c> puts it in the token. A matrix that
/// displayed differently from what the pipeline enforces would be worse than
/// the read-only one it replaced, because an administrator would believe it.
/// </remarks>
public sealed class RoleMatrixEditingTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string Password = "a-long-enough-password";

    private Guid _tenantId;
    private string _slug = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();

        var tenant = await factory.SeedActiveTenantAsync();

        _tenantId = tenant.Id;
        _slug = tenant.Slug;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Admin() => factory.CreateClientAs(
        Guid.NewGuid(), _tenantId, ["SamaajAdmin"], ["Roles.Manage"]);

    private static string Path(Guid roleId, string key) =>
        $"/v1/identity/roles/{roleId}/permissions/{key}";

    [Fact]
    public async Task A_Samaaj_can_take_a_permission_away_from_one_of_its_roles()
    {
        var response = await Admin().PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.ContentModerator, "Timeline.Moderate"),
            new { granted = false });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var matrix = await response.Content.ReadFromJsonAsync<JsonElement>();

        var moderator = matrix.GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == "ContentModerator");

        moderator.GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString())
            .Should().NotContain("Timeline.Moderate");
    }

    [Fact]
    public async Task The_change_reaches_the_token_and_not_only_the_screen()
    {
        // The whole point of the feature, and the thing a matrix screen alone
        // would not prove. A real member signs in after the change and the
        // permission is gone from what they actually carry.
        //
        // Member rather than ContentModerator, because registering is enough to
        // hold it - no role grant needed, so the test exercises the path a real
        // member takes.
        await RegisterAsync("ordinary@example.com");

        var before = await SignInPermissionsAsync("ordinary@example.com");

        before.Should().Contain("Timeline.Post");

        await Admin().PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.Member, "Timeline.Post"),
            new { granted = false });

        var after = await SignInPermissionsAsync("ordinary@example.com");

        after.Should().NotContain("Timeline.Post");
    }

    [Fact]
    public async Task A_permission_a_role_never_had_can_be_granted()
    {
        // Overrides go both ways. A Samaaj that wants its ordinary members to
        // be able to publish events can say so.
        await RegisterAsync("ordinary@example.com");

        (await SignInPermissionsAsync("ordinary@example.com"))
            .Should().NotContain("Events.Publish");

        await Admin().PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.Member, "Events.Publish"),
            new { granted = true });

        (await SignInPermissionsAsync("ordinary@example.com"))
            .Should().Contain("Events.Publish");
    }

    [Fact]
    public async Task One_Samaaj_change_does_not_reach_another()
    {
        // Overrides are per Samaaj. The platform defaults are never edited, so
        // a second Samaaj still sees what it always did.
        var other = await factory.SeedActiveTenantAsync("pune-samaaj");

        await Admin().PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.ContentModerator, "Timeline.Moderate"),
            new { granted = false });

        var elsewhere = factory.CreateClientAs(
            Guid.NewGuid(), other.Id, ["SamaajAdmin"], ["Roles.Manage"]);

        var matrix = await elsewhere.GetFromJsonAsync<JsonElement>("/v1/identity/roles");

        matrix.GetProperty("roles").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == "ContentModerator")
            .GetProperty("permissions").EnumerateArray()
            .Select(p => p.GetString())
            .Should().Contain("Timeline.Moderate");
    }

    [Fact]
    public async Task Setting_a_permission_back_to_its_default_removes_the_override()
    {
        // Rather than storing "same as the default", so a Samaaj that undoes a
        // change resumes tracking that default as it changes.
        var path = Path(AuthorizationCatalog.RoleIds.ContentModerator, "Timeline.Moderate");

        await Admin().PutAsJsonAsync(path, new { granted = false });
        await Admin().PutAsJsonAsync(path, new { granted = true });

        var rows = await factory.WithDbContextAsync(db =>
            db.RolePermissionOverrides.IgnoreQueryFilters().CountAsync());

        rows.Should().Be(0);
    }

    [Fact]
    public async Task SuperAdmin_cannot_be_edited_by_a_Samaaj()
    {
        var response = await Admin().PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.SuperAdmin, "Tenant.Manage"),
            new { granted = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Samaaj_administrator_cannot_be_stripped_of_Roles_Manage()
    {
        // The lock-out floor. Without it the screen refuses the administrator
        // who just used it, and nobody in the Samaaj can put it back.
        var response = await Admin().PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.SamaajAdmin, "Roles.Manage"),
            new { granted = false });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("title").GetString().Should().Be("Matrix.Protected");
    }

    [Fact]
    public async Task Granting_it_back_is_allowed_because_only_removal_locks_anybody_out()
    {
        var response = await Admin().PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.SamaajAdmin, "Roles.Manage"),
            new { granted = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_administrator_without_Roles_Manage_is_refused()
    {
        // Deliberately a different key from AdminUsers.Manage: inviting an
        // administrator hands somebody an existing bundle, this redefines it.
        var weaker = factory.CreateClientAs(
            Guid.NewGuid(), _tenantId, ["SamaajAdmin"], ["AdminUsers.Manage"]);

        var response = await weaker.PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.ContentModerator, "Timeline.Moderate"),
            new { granted = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Every_change_is_announced_for_the_audit_log()
    {
        await Admin().PutAsJsonAsync(
            Path(AuthorizationCatalog.RoleIds.ContentModerator, "Timeline.Moderate"),
            new { granted = false });

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "identity.role-matrix.changed.v1")
                .ToListAsync());

        outbox.Should().ContainSingle();
        outbox[0].Payload.Should().Contain("Timeline.Moderate");
    }

    [Fact]
    public async Task Repeating_a_change_is_success_and_writes_nothing_further()
    {
        var path = Path(AuthorizationCatalog.RoleIds.ContentModerator, "Timeline.Moderate");

        await Admin().PutAsJsonAsync(path, new { granted = false });
        var again = await Admin().PutAsJsonAsync(path, new { granted = false });

        again.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await factory.WithDbContextAsync(db =>
            db.RolePermissionOverrides.IgnoreQueryFilters().CountAsync());

        rows.Should().Be(1);
    }

    private async Task RegisterAsync(string identifier)
    {
        var notice = await factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/v1/identity/consent-notice");

        var registered = await factory.CreateClient().PostAsJsonAsync("/v1/identity/register", new
        {
            tenantSlug = _slug,
            fullName = "Ordinary Member",
            mobileOrEmail = identifier,
            password = Password,
            consentedPurposes = new[] { "Membership" },
            noticeVersion = notice.GetProperty("version").GetString(),
        });

        registered.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<IReadOnlyList<string>> SignInPermissionsAsync(string identifier)
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/v1/identity/login", new
        {
            mobileOrEmail = identifier,
            password = Password,
        });

        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var me = await client.GetFromJsonAsync<JsonElement>("/v1/identity/me");

        return [.. me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];
    }
}
