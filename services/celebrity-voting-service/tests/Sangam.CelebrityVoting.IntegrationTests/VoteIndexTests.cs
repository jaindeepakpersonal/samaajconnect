using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.CelebrityVoting.Domain.Campaigns;
using Sangam.CelebrityVoting.Infrastructure.Persistence;
using Xunit;

namespace Sangam.CelebrityVoting.IntegrationTests;

/// <summary>
/// Asserts the double-voting guarantee at the level it actually lives on.
/// </summary>
/// <remarks>
/// ConcurrentVotingTests proves the service behaves correctly when requests
/// race. It does not, on its own, prove <i>why</i>: a handler that happened to
/// serialise its callers would pass every one of those tests, and would then
/// stop holding the moment the service ran on two instances.
///
/// These two tests name the mechanism. They talk to the database directly,
/// past the handler and past the repository, so that the only thing that can
/// make them pass is the unique index on <c>(CampaignId, VoterMemberId)</c>
/// being present and being unique. Delete that index and these fail; that is
/// the whole point of them.
/// </remarks>
public sealed class VoteIndexTests(CelebrityVotingApiFactory factory)
    : IClassFixture<CelebrityVotingApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_votes_index_exists_and_is_unique()
    {
        // Read from the live schema rather than from the EF model: the model is
        // what we asked for, and this is what the database actually built.
        var definition = await factory.WithDbContextAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();

            await db.Database.OpenConnectionAsync();

            command.CommandText =
                "SELECT indexdef FROM pg_indexes "
                + "WHERE tablename = 'votes' AND indexname = 'ix_votes_campaign_id_voter_member_id';";

            return (string?)await command.ExecuteScalarAsync();
        });

        definition.Should().NotBeNull(
            "the index on (campaign_id, voter_member_id) is what prevents double voting");

        definition.Should().StartWith("CREATE UNIQUE INDEX",
            "a non-unique index on those columns would make the votes query fast "
            + "and would stop preventing anything");
    }

    [Fact]
    public async Task A_second_vote_from_one_member_is_refused_by_the_database()
    {
        // Straight at the table, on two separate contexts, so nothing in the
        // application layer can be what refuses the second write.
        var campaignId = Guid.NewGuid();
        var voterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(new Vote(campaignId, Guid.NewGuid(), voterId, now));

        var second = async () => await InsertAsync(new Vote(campaignId, Guid.NewGuid(), voterId, now));

        var thrown = await second.Should().ThrowAsync<DbUpdateException>(
            "the index, not the application, is what refuses it");

        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(
                PostgresErrorCodes.UniqueViolation,
                "VoteRepository.TryCastAsync catches exactly this SQLSTATE to report "
                + "a duplicate press as accepted=false rather than as a 500");

        // And the first vote is still the one that stands.
        var votes = await factory.WithDbContextAsync(db =>
            db.Votes.CountAsync(v => v.CampaignId == campaignId));

        votes.Should().Be(1);
    }

    private async Task InsertAsync(Vote vote)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CelebrityVotingDbContext>();

        db.Votes.Add(vote);

        await db.SaveChangesAsync();
    }
}
