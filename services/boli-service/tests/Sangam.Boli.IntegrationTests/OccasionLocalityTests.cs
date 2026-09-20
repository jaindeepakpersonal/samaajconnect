using System.Net.Http.Json;
using FluentAssertions;
using Sangam.Boli.Application.Auctions;
using Sangam.Boli.Application.Security;
using Xunit;

namespace Sangam.Boli.IntegrationTests;

/// <summary>
/// Boli becoming locality-taggable: a tag/filter dimension only in this
/// phase, additive and non-breaking for every occasion that carries no
/// locality at all. See boli-service's own CLAUDE.md.
/// </summary>
public sealed class OccasionLocalityTests(BoliApiFactory factory)
    : IClassFixture<BoliApiFactory>, IAsyncLifetime
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid ManagerId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Manager() => factory.CreateClientAs(
        ManagerId, Tenant, [Roles.BoliManager], [PermissionKeys.BoliManage, PermissionKeys.MembersRead]);

    private async Task<Guid> CreateOccasionAsync(Guid? localityId, string title)
    {
        var response = await Manager().PostAsJsonAsync("/v1/boli/occasions", new
        {
            title,
            description = (string?)null,
            occasionDate = "2026-09-10",
            localityId,
        });

        var occasion = await response.Content.ReadFromJsonAsync<OccasionResponse>();

        return occasion!.Id;
    }

    [Fact]
    public async Task An_occasion_created_without_a_locality_still_works_exactly_as_before()
    {
        var occasionId = await CreateOccasionAsync(localityId: null, "Samaaj-wide Paryushan");

        var got = await Manager().GetFromJsonAsync<OccasionDetailResponse>($"/v1/boli/occasions/{occasionId}");

        got!.LocalityId.Should().BeNull();

        var listed = await Manager().GetFromJsonAsync<List<OccasionResponse>>("/v1/boli/occasions");

        listed.Should().ContainSingle(o => o.Id == occasionId && o.LocalityId == null);
    }

    [Fact]
    public async Task An_occasion_can_be_created_for_a_locality()
    {
        var localityId = Guid.NewGuid();
        var occasionId = await CreateOccasionAsync(localityId, "Hiran Magri Paryushan");

        var got = await Manager().GetFromJsonAsync<OccasionDetailResponse>($"/v1/boli/occasions/{occasionId}");

        got!.LocalityId.Should().Be(localityId);
    }

    [Fact]
    public async Task Listing_occasions_can_be_narrowed_to_one_locality()
    {
        var localityA = Guid.NewGuid();
        var localityB = Guid.NewGuid();

        var occasionA = await CreateOccasionAsync(localityA, "Locality A occasion");
        var occasionB = await CreateOccasionAsync(localityB, "Locality B occasion");
        var occasionWide = await CreateOccasionAsync(null, "Samaaj-wide occasion");

        var narrowed = await Manager()
            .GetFromJsonAsync<List<OccasionResponse>>($"/v1/boli/occasions?localityId={localityA}");

        narrowed.Should().ContainSingle(o => o.Id == occasionA);
        narrowed.Should().NotContain(o => o.Id == occasionB || o.Id == occasionWide);

        var everything = await Manager().GetFromJsonAsync<List<OccasionResponse>>("/v1/boli/occasions");

        everything.Should().HaveCount(3, "no filter means every locality, including Samaaj-wide occasions");
    }

    [Fact]
    public async Task Active_Boli_can_be_narrowed_to_one_localitys_occasion()
    {
        var localityA = Guid.NewGuid();
        var localityB = Guid.NewGuid();
        var manager = Manager();
        var now = factory.Clock.UtcNow;

        var boliInA = await OpenBoliUnderAsync(localityA, "Boli in A");
        var boliInB = await OpenBoliUnderAsync(localityB, "Boli in B");
        var boliWide = await OpenBoliUnderAsync(null, "Samaaj-wide Boli");

        var narrowed = await manager
            .GetFromJsonAsync<List<BoliResponse>>($"/v1/boli/boli/active?localityId={localityA}");

        narrowed.Should().ContainSingle(b => b.Id == boliInA);
        narrowed.Should().NotContain(b => b.Id == boliInB || b.Id == boliWide);

        var everything = await manager.GetFromJsonAsync<List<BoliResponse>>("/v1/boli/boli/active");

        everything.Should().HaveCount(3, "no filter means every locality, including Samaaj-wide occasions");

        async Task<Guid> OpenBoliUnderAsync(Guid? localityId, string title)
        {
            var occasion = await (await manager.PostAsJsonAsync("/v1/boli/occasions", new
            {
                title,
                description = (string?)null,
                occasionDate = "2026-09-10",
                localityId,
            })).Content.ReadFromJsonAsync<OccasionResponse>();

            var type = await (await manager.PostAsJsonAsync(
                $"/v1/boli/occasions/{occasion!.Id}/boli-types",
                new { name = title, description = (string?)null }))
                .Content.ReadFromJsonAsync<BoliTypeResponse>();

            var lot = await (await manager.PostAsJsonAsync($"/v1/boli/occasions/{occasion.Id}/boli", new
            {
                boliTypeId = type!.Id,
                title,
                startAt = now.AddMinutes(-5),
                endAt = now.AddHours(2),
                startingAmount = 1_000_00L,
                minIncrement = 500_00L,
                eligibilityRule = (string?)null,
            })).Content.ReadFromJsonAsync<BoliResponse>();

            return lot!.Id;
        }
    }
}
