using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sangam.Trust.Domain.Elections;
using Sangam.Trust.Infrastructure.Persistence;
using Xunit;

namespace Sangam.Trust.IntegrationTests;

/// <summary>
/// Asserts the double-voting guarantee at the level it actually lives on -
/// the identical proof volunteer-groups-service's and celebrity-voting-
/// service's own <c>VoteIndexTests</c> already run, copied here because this
/// service's electorate is a Trust's roster rather than a Samaaj or a group,
/// but the guarantee is the same shape.
/// </summary>
/// <remarks>
/// These talk to the database directly, past the handler and past the
/// repository, so that only the unique indexes on
/// <c>(ElectionId, VoterMemberId, Choice)</c> and
/// <c>(ElectionId, VoterMemberId, CandidateId)</c> being present and unique can
/// make them pass. Delete either and these fail; that is the point of them.
/// </remarks>
public sealed class TrustVoteIndexTests(TrustApiFactory factory)
    : IClassFixture<TrustApiFactory>, IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("ix_trust_election_votes_election_id_voter_member_id_choice")]
    [InlineData("ix_trust_election_votes_election_id_voter_member_id_candidate_")]
    public async Task The_votes_indexes_exist_and_are_unique(string index)
    {
        var definition = await factory.WithDbContextAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();

            await db.Database.OpenConnectionAsync();

            command.CommandText =
                "SELECT indexdef FROM pg_indexes "
                + "WHERE tablename = 'trust_election_votes' "
                + $"AND indexname = '{index}';";

            return (string?)await command.ExecuteScalarAsync();
        });

        definition.Should().NotBeNull("this index is what holds one ballot per voter");

        definition.Should().StartWith("CREATE UNIQUE INDEX",
            "a non-unique index on those columns would make the votes query fast "
            + "and would stop preventing anything");
    }

    [Fact]
    public async Task A_second_ballot_from_one_member_is_refused_by_the_database()
    {
        var electionId = Guid.NewGuid();
        var voterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(new Vote(electionId, Guid.NewGuid(), voterId, 1, now));

        // Every ballot starts at choice 1, so a second one collides on it.
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

    [Fact]
    public async Task One_ballot_cannot_mark_a_candidate_twice()
    {
        var electionId = Guid.NewGuid();
        var voterId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await InsertAsync(new Vote(electionId, candidateId, voterId, 1, now));

        var again = async () => await InsertAsync(new Vote(electionId, candidateId, voterId, 2, now));

        (await again.Should().ThrowAsync<DbUpdateException>()).Which.InnerException
            .Should().BeOfType<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    private async Task InsertAsync(Vote vote)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrustDbContext>();

        db.Votes.Add(vote);

        await db.SaveChangesAsync();
    }
}
