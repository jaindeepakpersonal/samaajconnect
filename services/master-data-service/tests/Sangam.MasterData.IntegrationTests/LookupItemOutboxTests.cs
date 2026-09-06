using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.MasterData.Application.Security;
using Xunit;

namespace Sangam.MasterData.IntegrationTests;

/// <summary>
/// Proves the two mechanisms every service on this platform relies on: a
/// unique index refuses a duplicate lookup code, and a state change lands in
/// the Outbox in the same transaction (root CLAUDE.md §5).
/// </summary>
public sealed class LookupItemOutboxTests(MasterDataApiFactory factory)
    : IClassFixture<MasterDataApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Creating_a_lookup_item_writes_an_outbox_row_in_the_same_transaction()
    {
        var admin = factory.CreateClientAs(
            Guid.NewGuid(), Guid.NewGuid(), ["SuperAdmin"], [PermissionKeys.MasterDataManage]);

        var response = await admin.PostAsJsonAsync(
            "/v1/master-data/lookup-items",
            new { Category = "Country", Code = "IN", Label = "India", SortOrder = 1 });

        response.EnsureSuccessStatusCode();

        var outboxRowExists = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AnyAsync(m => m.Topic == "master-data.lookup-item.created.v1"));

        outboxRowExists.Should().BeTrue(
            "the Outbox row is what guarantees the event is eventually published, "
            + "written in the same transaction as the state change that raised it");
    }

    [Fact]
    public async Task A_duplicate_code_within_the_same_category_is_refused()
    {
        var admin = factory.CreateClientAs(
            Guid.NewGuid(), Guid.NewGuid(), ["SuperAdmin"], [PermissionKeys.MasterDataManage]);

        var first = await admin.PostAsJsonAsync(
            "/v1/master-data/lookup-items",
            new { Category = "Country", Code = "IN", Label = "India", SortOrder = 1 });
        first.EnsureSuccessStatusCode();

        var second = await admin.PostAsJsonAsync(
            "/v1/master-data/lookup-items",
            new { Category = "Country", Code = "IN", Label = "India (duplicate)", SortOrder = 2 });

        second.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_same_code_is_allowed_again_in_a_different_category()
    {
        var admin = factory.CreateClientAs(
            Guid.NewGuid(), Guid.NewGuid(), ["SuperAdmin"], [PermissionKeys.MasterDataManage]);

        var country = await admin.PostAsJsonAsync(
            "/v1/master-data/lookup-items",
            new { Category = "Country", Code = "M", Label = "Malta", SortOrder = 1 });
        country.EnsureSuccessStatusCode();

        var gender = await admin.PostAsJsonAsync(
            "/v1/master-data/lookup-items",
            new { Category = "Gender", Code = "M", Label = "Male", SortOrder = 1 });

        gender.StatusCode.Should().Be(System.Net.HttpStatusCode.Created,
            "'M' means one thing among countries and a different thing among genders - "
            + "the uniqueness constraint is scoped per category, not global");
    }
}
