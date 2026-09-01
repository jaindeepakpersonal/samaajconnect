using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Boli.Domain.Auctions;
using Sangam.Boli.Infrastructure.Persistence;
using Xunit;

namespace Sangam.Boli.IntegrationTests;

/// <summary>
/// Asserts the bidding guarantee at the level it actually lives on.
/// </summary>
/// <remarks>
/// <see cref="ConcurrentBiddingTests"/> proves the service behaves correctly
/// when requests race. It does not, on its own, prove <i>why</i>: a row lock is
/// a convention that a future code path can forget, and a handler that happened
/// to serialise its callers would pass all of those tests and then stop holding
/// the moment this service ran on two instances.
///
/// These tests name the mechanism. They talk to the database directly, past the
/// handler and past the repository, so the only thing that can make them pass is
/// the unique index on <c>(boli_id, amount)</c> being present and being unique.
/// Delete it and these fail; that is the whole point of them.
/// </remarks>
public sealed class BidIndexTests(BoliApiFactory factory)
    : IClassFixture<BoliApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_bids_index_exists_and_is_unique()
    {
        // Read from the live schema rather than from the EF model: the model is
        // what we asked for, and this is what the database actually built.
        var definition = await factory.WithDbContextAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();

            await db.Database.OpenConnectionAsync();

            command.CommandText =
                "SELECT indexdef FROM pg_indexes "
                + "WHERE tablename = 'bids' AND indexname = 'ix_bids_boli_id_amount';";

            return (string?)await command.ExecuteScalarAsync();
        });

        definition.Should().NotBeNull(
            "the index on (boli_id, amount) is what keeps one Boli to one highest bid");

        definition.Should().StartWith("CREATE UNIQUE INDEX",
            "a non-unique index on those columns would make the bid history fast "
            + "and would stop preventing anything");
    }

    [Fact]
    public async Task Two_bids_of_the_same_amount_on_one_Boli_are_refused_by_the_database()
    {
        // Straight at the table, on two separate contexts, so nothing in the
        // application layer can be what refuses the second write.
        var boliId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(Bid.Place(tenantId, boliId, Guid.NewGuid(), 15_600_00, now));

        var second = async () =>
            await InsertAsync(Bid.Place(tenantId, boliId, Guid.NewGuid(), 15_600_00, now));

        var thrown = await second.Should().ThrowAsync<DbUpdateException>(
            "the index, not the application, is what refuses it");

        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

        // Two bids at one amount would leave "the highest bid" with no single
        // referent, and the winner decided by whichever row sorted first.
        var bids = await factory.WithDbContextAsync(db =>
            db.Bids.IgnoreQueryFilters().CountAsync(b => b.BoliId == boliId));

        bids.Should().Be(1);
    }

    [Fact]
    public async Task The_same_amount_on_a_different_Boli_is_fine()
    {
        // The index is per Boli, deliberately. Two Boli at one occasion selling
        // for the same amount is ordinary.
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(Bid.Place(tenantId, Guid.NewGuid(), Guid.NewGuid(), 15_600_00, now));
        await InsertAsync(Bid.Place(tenantId, Guid.NewGuid(), Guid.NewGuid(), 15_600_00, now));

        var bids = await factory.WithDbContextAsync(db =>
            db.Bids.IgnoreQueryFilters().CountAsync(b => b.Amount == 15_600_00));

        bids.Should().Be(2);
    }

    [Fact]
    public async Task A_Boli_can_hold_only_one_result()
    {
        // A second result would leave "the result" with no referent, the same
        // way a second published ranking would in celebrity-voting-service.
        var boliId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var bid = Bid.Place(tenantId, boliId, Guid.NewGuid(), 15_600_00, now);

        await InsertResultAsync(BoliResult.Record(tenantId, boliId, bid, Guid.NewGuid(), now));

        var second = async () =>
            await InsertResultAsync(BoliResult.Record(tenantId, boliId, bid, Guid.NewGuid(), now));

        var thrown = await second.Should().ThrowAsync<DbUpdateException>();

        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    private async Task InsertAsync(Bid bid)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoliDbContext>();

        db.Bids.Add(bid);

        await db.SaveChangesAsync();
    }

    private async Task InsertResultAsync(BoliResult result)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BoliDbContext>();

        db.Results.Add(result);

        await db.SaveChangesAsync();
    }
}
