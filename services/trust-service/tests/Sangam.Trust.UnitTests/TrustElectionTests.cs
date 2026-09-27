using FluentAssertions;
using Sangam.Trust.Domain.Elections;
using Sangam.Trust.Domain.Elections.Events;
using Xunit;

namespace Sangam.Trust.UnitTests;

/// <summary>
/// An election for one Trust role: nominate, approve, vote, close, publish.
/// </summary>
public sealed class TrustElectionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid TrustId = Guid.NewGuid();
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static TrustElection Election(DateTimeOffset? now = null, int seats = 1) =>
        TrustElection.Open(
            TenantId,
            TrustId,
            RoleId,
            seats,
            nominationStartAt: now ?? Now,
            nominationEndAt: (now ?? Now).AddDays(7),
            votingStartAt: (now ?? Now).AddDays(7),
            votingEndAt: (now ?? Now).AddDays(14),
            now ?? Now);

    [Fact]
    public void Opening_names_the_role_and_its_seats()
    {
        var election = Election(seats: 3);

        election.RoleId.Should().Be(RoleId);
        election.Seats.Should().Be(3);
    }

    [Fact]
    public void An_election_fills_at_least_one_seat()
    {
        var open = () => Election(seats: 0);

        open.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Starts_a_draft()
    {
        Election().Status.Should().Be(TrustElectionStatus.Draft);
    }

    // ---- The forward-only sequence -----------------------------------------

    [Fact]
    public void Moves_forward_one_step_at_a_time()
    {
        var election = Election();

        election.MoveTo(TrustElectionStatus.NominationsOpen, Now).Should().BeTrue();
        election.MoveTo(TrustElectionStatus.VotingOpen, Now).Should().BeTrue();
        election.MoveTo(TrustElectionStatus.Closed, Now).Should().BeTrue();
        election.Status.Should().Be(TrustElectionStatus.Closed);
    }

    [Fact]
    public void Cannot_skip_a_step()
    {
        var election = Election();

        election.MoveTo(TrustElectionStatus.VotingOpen, Now).Should().BeFalse();
        election.Status.Should().Be(TrustElectionStatus.Draft);
    }

    [Fact]
    public void Cannot_go_backwards()
    {
        var election = Election();
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);
        election.MoveTo(TrustElectionStatus.VotingOpen, Now);

        election.MoveTo(TrustElectionStatus.NominationsOpen, Now).Should().BeFalse();
        election.Status.Should().Be(TrustElectionStatus.VotingOpen);
    }

    [Fact]
    public void An_election_cannot_be_moved_straight_to_Published()
    {
        // Publishing is its own command - it has to pick and freeze a winner.
        var election = Election();
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);
        election.MoveTo(TrustElectionStatus.VotingOpen, Now);
        election.MoveTo(TrustElectionStatus.Closed, Now);

        election.MoveTo(TrustElectionStatus.Published, Now).Should().BeFalse();
    }

    // ---- Nominating ---------------------------------------------------------

    [Fact]
    public void Nominating_requires_nominations_to_be_open()
    {
        var election = Election();

        election.Nominate(Guid.NewGuid(), AdminId, Now).Should().BeNull();
    }

    [Fact]
    public void Nominating_the_same_member_twice_is_a_no_op()
    {
        var election = Election();
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);
        var memberId = Guid.NewGuid();

        election.Nominate(memberId, AdminId, Now).Should().NotBeNull();
        election.Nominate(memberId, Guid.NewGuid(), Now).Should().BeNull();
        election.Candidates.Should().ContainSingle();
    }

    // ---- The ballot -----------------------------------------------------------

    [Fact]
    public void The_ballot_is_approved_candidates_only()
    {
        var election = Election();
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);

        var approved = election.Nominate(Guid.NewGuid(), AdminId, Now)!;
        var waiting = election.Nominate(Guid.NewGuid(), AdminId, Now)!;

        election.ApproveCandidate(approved.Id, AdminId, Now);

        election.Ballot.Should().ContainSingle().Which.Id.Should().Be(approved.Id);
        election.FindCandidate(waiting.Id).Should().NotBeNull("a nomination is not a candidate until approved");
    }

    [Fact]
    public void Removing_a_candidate_is_refused_once_voting_has_opened()
    {
        var election = Election();
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);
        var candidate = election.Nominate(Guid.NewGuid(), AdminId, Now)!;
        election.ApproveCandidate(candidate.Id, AdminId, Now);
        election.MoveTo(TrustElectionStatus.VotingOpen, Now);

        election.RejectCandidate(candidate.Id, Now).Should()
            .BeFalse("removing now would discard votes already cast for them");
    }

    // ---- Picking the winners ----------------------------------------------

    [Fact]
    public void The_highest_tally_wins()
    {
        var election = Election();
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);
        var leader = election.Nominate(Guid.NewGuid(), AdminId, Now)!;
        var runnerUp = election.Nominate(Guid.NewGuid(), AdminId, Now)!;
        election.ApproveCandidate(leader.Id, AdminId, Now);
        election.ApproveCandidate(runnerUp.Id, AdminId, Now);

        var tally = new Dictionary<Guid, int> { [leader.Id] = 5, [runnerUp.Id] = 3 };

        election.WinnersBy(tally).Should().ContainSingle().Which.Id.Should().Be(leader.Id);
    }

    [Fact]
    public void As_many_win_as_there_are_seats_in_tally_order()
    {
        var election = Election(seats: 2);
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);
        var first = election.Nominate(Guid.NewGuid(), AdminId, Now)!;
        var second = election.Nominate(Guid.NewGuid(), AdminId, Now.AddMinutes(1))!;
        var third = election.Nominate(Guid.NewGuid(), AdminId, Now.AddMinutes(2))!;

        foreach (var candidate in new[] { first, second, third })
        {
            election.ApproveCandidate(candidate.Id, AdminId, Now);
        }

        var tally = new Dictionary<Guid, int> { [first.Id] = 1, [second.Id] = 4, [third.Id] = 3 };

        election.WinnersBy(tally).Select(c => c.Id).Should().Equal(second.Id, third.Id);
    }

    [Fact]
    public void A_short_ballot_seats_everybody_on_it()
    {
        var election = Election(seats: 3);
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);
        var only = election.Nominate(Guid.NewGuid(), AdminId, Now)!;
        election.ApproveCandidate(only.Id, AdminId, Now);

        election.WinnersBy(new Dictionary<Guid, int>()).Should().ContainSingle().Which.Id.Should().Be(only.Id);
    }

    [Fact]
    public void A_tie_is_broken_by_earliest_nomination()
    {
        // Created deliberately out of order, so this distinguishes "the
        // tie-break is working" from "candidates happened to already be in
        // the right order".
        var election = Election();
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);

        var later = election.Nominate(Guid.NewGuid(), AdminId, Now.AddHours(2))!;
        var earlier = election.Nominate(Guid.NewGuid(), AdminId, Now.AddHours(1))!;
        election.ApproveCandidate(later.Id, AdminId, Now);
        election.ApproveCandidate(earlier.Id, AdminId, Now);

        var tally = new Dictionary<Guid, int> { [later.Id] = 2, [earlier.Id] = 2 };

        election.WinnersBy(tally).Should().ContainSingle().Which.Id.Should().Be(earlier.Id);
    }

    [Fact]
    public void No_candidates_means_no_winner()
    {
        Election().WinnersBy(new Dictionary<Guid, int>()).Should().BeEmpty();
    }

    // ---- Publishing -----------------------------------------------------------

    [Fact]
    public void Publishing_requires_the_election_to_be_closed()
    {
        var election = Election();

        election.Publish([Guid.NewGuid()], Now).Should().BeFalse();
    }

    [Fact]
    public void Publishing_freezes_the_winners_and_cannot_happen_twice()
    {
        var election = Election();
        election.MoveTo(TrustElectionStatus.NominationsOpen, Now);
        election.MoveTo(TrustElectionStatus.VotingOpen, Now);
        election.MoveTo(TrustElectionStatus.Closed, Now);

        var winnerIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        election.Publish(winnerIds, Now).Should().BeTrue();
        election.Status.Should().Be(TrustElectionStatus.Published);
        election.WinnerMemberIds.Should().Equal(winnerIds);
        election.PublishedAt.Should().Be(Now);

        var raised = election.DomainEvents.OfType<TrustElectionResultsPublishedDomainEvent>().Should().ContainSingle().Subject;
        raised.RoleId.Should().Be(RoleId);
        raised.WinnerMemberIds.Should().Equal(winnerIds);

        election.Publish([Guid.NewGuid()], Now).Should()
            .BeFalse("a second publish would leave \"the result\" with no single referent");
        election.WinnerMemberIds.Should().Equal(winnerIds, "the first result is the one that stands");
    }
}
