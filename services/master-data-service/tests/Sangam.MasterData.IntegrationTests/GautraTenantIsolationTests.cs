using System.Net.Http.Json;
using FluentAssertions;
using Sangam.MasterData.Application.Gautras;
using Sangam.MasterData.Application.Security;
using Xunit;

namespace Sangam.MasterData.IntegrationTests;

/// <summary>
/// Proves the global tenant query filter actually isolates Gautra lists
/// between Samaajs - the one tenant-scoped aggregate this service owns.
/// </summary>
/// <remarks>
/// This is exactly the shape root CLAUDE.md §6 calls out: the query filter is
/// what a read relies on, but a write also re-validates the target's tenant id
/// independently (TenantWriteGuard, proven indirectly here by the fact that a
/// Samaaj Admin can never even load another Samaaj's Gautra to attempt writing
/// to it).
/// </remarks>
public sealed class GautraTenantIsolationTests(MasterDataApiFactory factory)
    : IClassFixture<MasterDataApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_samaaj_admin_cannot_see_another_samaajs_gautra_list()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var adminA = factory.CreateClientAs(
            Guid.NewGuid(), tenantA, ["SamaajAdmin"], [PermissionKeys.GautraManage]);
        var adminB = factory.CreateClientAs(
            Guid.NewGuid(), tenantB, ["SamaajAdmin"], [PermissionKeys.GautraManage]);

        var createResponse = await adminA.PostAsJsonAsync(
            "/v1/master-data/gautras", new { Name = "Kasliwal" });

        createResponse.EnsureSuccessStatusCode();

        var listAsA = await adminA.GetFromJsonAsync<List<GautraResponse>>(
            "/v1/master-data/gautras?includeInactive=false");
        var listAsB = await adminB.GetFromJsonAsync<List<GautraResponse>>(
            "/v1/master-data/gautras?includeInactive=false");

        listAsA.Should().ContainSingle(g => g.Name == "Kasliwal");
        listAsB.Should().BeEmpty(
            "the global query filter scopes every Gautra read to the caller's own tenant");
    }

    [Fact]
    public async Task A_samaaj_admin_cannot_update_a_gautra_that_belongs_to_another_samaaj()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var adminA = factory.CreateClientAs(
            Guid.NewGuid(), tenantA, ["SamaajAdmin"], [PermissionKeys.GautraManage]);
        var adminB = factory.CreateClientAs(
            Guid.NewGuid(), tenantB, ["SamaajAdmin"], [PermissionKeys.GautraManage]);

        var created = await (await adminA.PostAsJsonAsync(
                "/v1/master-data/gautras", new { Name = "Bafna" }))
            .Content.ReadFromJsonAsync<GautraResponse>();

        var attempt = await adminB.PutAsJsonAsync(
            $"/v1/master-data/gautras/{created!.Id}", new { Name = "Renamed" });

        attempt.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound,
            "the row is invisible to tenant B's query filter, not merely forbidden - "
            + "the two must not be distinguishable from a real deletion");
    }
}
