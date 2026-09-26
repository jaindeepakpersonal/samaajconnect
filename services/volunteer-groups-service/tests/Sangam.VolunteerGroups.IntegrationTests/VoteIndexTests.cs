using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.VolunteerGroups.Domain.Elections;
using Sangam.VolunteerGroups.Infrastructure.Persistence;
using Xunit;

namespace Sangam.VolunteerGroups.IntegrationTests;

/// <summary>
/// Asserts the double-voting guarantee at the level it actually lives on -
/// the identical proof celebrity-voting-service's own <c>VoteIndexTests</c>
/// already runs, copied here because this service's electorate is a group
/// rather than a Samaaj but the guarantee is the same shape.
/// </summary>
/// <remarks>
/// These talk to the database directly, past the handler and past the
/// repository, so that only the two unique indexes on <c>election_votes</c>
/// being present and unique can make them pass. Delete either and these fail;
/// that is the point of them. A ballot's picks are numbered from 1, so
/// <c>(election, voter, choice)</c> is what refuses a second ballot, and
/// <c>(election, voter, candidate)</c> is what refuses one candidate picked twice.
/// </remarks>
public sealed class VoteIndexTests(VolunteerGroupsApiFactory factory)
    : IClassFixture<VolunteerGroupsApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("ix_election_votes_election_id_voter_member_id_choice")]
    [InlineData("ix_election_votes_election_id_voter_member_id_candidate_id")]
    public async Task The_votes_indexes_exist_and_are_unique(string indexName)
    {
        var definition = await factory.WithDbContextAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();

            await db.Database.OpenConnectionAsync();

            command.CommandText =
                "SELECT indexdef FROM pg_indexes "
                + "WHERE tablename = 'election_votes' "
                + $"AND indexname = '{indexName}';";

            return (string?)await command.ExecuteScalarAsync();
        });

        definition.Should().NotBeNull($"{indexName} is what prevents double voting");

        definition.Should().StartWith("CREATE UNIQUE INDEX",
            "a non-unique index on those columns would make the votes query fast "
            + "and would stop preventing anything");
    }

    [Fact]
    public async Task One_ballot_may_pick_several_candidates()
    {
        var electionId = Guid.NewGuid();
        var voterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(
            new Vote(electionId, Guid.NewGuid(), voterId, 1, now),
            new Vote(electionId, Guid.NewGuid(), voterId, 2, now));

        (await factory.WithDbContextAsync(db => db.Votes.CountAsync(v => v.ElectionId == electionId)))
            .Should().Be(2);
    }

    [Fact]
    public async Task One_candidate_picked_twice_is_refused_by_the_database()
    {
        var electionId = Guid.NewGuid();
        var voterId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var twice = async () => await InsertAsync(
            new Vote(electionId, candidateId, voterId, 1, now),
            new Vote(electionId, candidateId, voterId, 2, now));

        (await twice.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_second_ballot_from_one_member_is_refused_by_the_database()
    {
        var electionId = Guid.NewGuid();
        var voterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(new Vote(electionId, Guid.NewGuid(), voterId, 1, now));

        // A second ballot for somebody else still starts at choice 1.
        var second = async () => await InsertAsync(new Vote(electionId, Guid.NewGuid(), voterId, 1, now));

        var thrown = await second.Should().ThrowAsync<DbUpdateException>(
            "the index, not the application, is what refuses it");

        thrown.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(
                PostgresErrorCodes.UniqueViolation,
                "VoteRepository.TryCastAsync catches exactly this SQLSTATE to report "
                + "a duplicate press as accepted=false rather than as a 500");

        var votes = await factory.WithDbContextAsync(db =>
            db.Votes.CountAsync(v => v.ElectionId == electionId));

        votes.Should().Be(1);
    }

    private async Task InsertAsync(params Vote[] ballot)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VolunteerGroupsDbContext>();

        db.Votes.AddRange(ballot);

        await db.SaveChangesAsync();
    }
}
