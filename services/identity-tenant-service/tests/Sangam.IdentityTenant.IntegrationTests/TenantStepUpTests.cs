using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;
using Sangam.IdentityTenant.Application.Security;
using Xunit;

namespace Sangam.IdentityTenant.IntegrationTests;

/// <summary>
/// Deactivating and archiving a Samaaj re-ask for the caller's own password,
/// end to end.
/// </summary>
/// <remarks>
/// Taking a Samaaj out of service signs out every one of its members at their
/// next refresh, and archiving cannot be undone at all. Erasing a single
/// account already asked for a password; these did not.
/// </remarks>
public sealed class TenantStepUpTests(IdentityTenantApiFactory factory)
    : IClassFixture<IdentityTenantApiFactory>, IAsyncLifetime
{
    private const string TenantsUrl = "/v1/identity/tenants";

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(HttpClient Admin, Guid TenantId)> ActiveSamaajAsync()
    {
        var tenant = await factory.SeedActiveTenantAsync();

        return (await factory.CreateSuperAdminClientAsync(), tenant.Id);
    }

    private static Task<HttpResponseMessage> ChangeAsync(
        HttpClient admin, Guid id, string status, string? password) =>
        admin.PatchAsJsonAsync($"{TenantsUrl}/{id}/status", new { status, password });

    /// <summary>
    /// Read from the database rather than through <c>by-id</c>, which answers
    /// 404 for an archived Samaaj - correctly, but that makes it useless as a
    /// probe for exactly the status these tests care most about.
    /// </summary>
    private async Task<string> StatusOfAsync(Guid id) =>
        (await factory.WithDbContextAsync(db => db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => t.Status)
            .FirstAsync())).ToString();

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Archived")]
    public async Task Taking_a_Samaaj_out_of_service_without_the_password_is_refused(string status)
    {
        var (admin, id) = await ActiveSamaajAsync();

        var response = await ChangeAsync(admin, id, status, password: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StatusOfAsync(id)).Should().Be("Active");
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_changes_nothing()
    {
        var (admin, id) = await ActiveSamaajAsync();

        var response = await ChangeAsync(admin, id, "Inactive", "not-the-password");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // A distinct code, so a portal can say "that password is not correct"
        // on the screen the admin is already on rather than treating it as a
        // blanket refusal.
        problem.GetProperty("title").GetString()
            .Should().Be(IStepUpAuthentication.StepUpFailedCode);

        (await StatusOfAsync(id)).Should().Be("Active");
    }

    [Fact]
    public async Task A_wrong_password_is_never_answered_with_401()
    {
        // The one status code this endpoint must not return. The portals'
        // interceptor treats a 401 as an expired access token: it renews the
        // token and retries the original request, so a mistyped password would
        // resubmit "deactivate this Samaaj".
        var (admin, id) = await ActiveSamaajAsync();

        var response = await ChangeAsync(admin, id, "Inactive", "not-the-password");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Inactive")]
    [InlineData("Archived")]
    public async Task The_right_password_goes_through(string status)
    {
        var (admin, id) = await ActiveSamaajAsync();

        var response = await ChangeAsync(
            admin, id, status, IdentityTenantApiFactory.BootstrapPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StatusOfAsync(id)).Should().Be(status);
    }

    [Fact]
    public async Task Bringing_a_Samaaj_back_into_service_needs_no_password()
    {
        // Deliberately asymmetric: activating restores service and is undone by
        // the very call that undid it. A step-up on the harmless direction only
        // teaches people to type their password without reading the screen.
        var (admin, id) = await ActiveSamaajAsync();

        (await ChangeAsync(admin, id, "Inactive", IdentityTenantApiFactory.BootstrapPassword))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ChangeAsync(admin, id, "Active", password: null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await StatusOfAsync(id)).Should().Be("Active");
    }

    [Fact]
    public async Task A_password_does_not_substitute_for_the_permission()
    {
        // The step-up is on top of authorization, not instead of it. A caller
        // without Tenant.Manage is refused before the password is ever read.
        var tenant = await factory.SeedActiveTenantAsync();
        var member = factory.CreateClientAs(Guid.NewGuid(), ["Member"], []);

        var response = await ChangeAsync(
            member, tenant.Id, "Inactive", IdentityTenantApiFactory.BootstrapPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StatusOfAsync(tenant.Id)).Should().Be("Active");
    }
}
