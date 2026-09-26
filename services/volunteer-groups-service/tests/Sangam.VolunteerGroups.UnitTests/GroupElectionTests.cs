using FluentAssertions;
using Sangam.VolunteerGroups.Domain.Elections;
using Sangam.VolunteerGroups.Domain.Elections.Events;
using Xunit;

namespace Sangam.VolunteerGroups.UnitTests;

/// <summary>
/// An election for one volunteer-group position: nominate, approve, vote,
/// close, publish.
/// </summary>
public sealed class GroupElectionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid PresidentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid RoleId = Guid.NewGuid();

    private static GroupElection Election(DateTimeOffset? now = null, int seats = 1) =>
        GroupElection.Open(
            TenantId,
            GroupId,
            RoleId,
            "  Secretary  ",
            seats,
            nominationStartAt: now ?? Now,
            nominationEndAt: (now ?? Now).AddDays(7),
            votingStartAt: (now ?? Now).AddDays(7),
            votingEndAt: (now ?? Now).AddDays(14),
            now ?? Now);

    [Fact]
    public void Opening_trims_the_position_title()
    {
        Election().PositionTitle.Should().Be("Secretary");
    }

    [Fact]
    public void Starts_a_draft()
    {
        Election().Status.Should().Be(ElectionStatus.Draft);
    }

    // ---- The forward-only sequence -----------------------------------------

    [Fact]
    public void Moves_forward_one_step_at_a_time()
    {
        var election = Election();

        election.MoveTo(ElectionStatus.NominationsOpen, Now).Should().BeTrue();
        election.MoveTo(ElectionStatus.VotingOpen, Now).Should().BeTrue();
        election.MoveTo(ElectionStatus.Closed, Now).Should().BeTrue();
        election.Status.Should().Be(ElectionStatus.Closed);
    }

    [Fact]
    public void Cannot_skip_a_step()
    {
        var election = Election();

        election.MoveTo(ElectionStatus.VotingOpen, Now).Should().BeFalse();
        election.Status.Should().Be(ElectionStatus.Draft);
    }

    [Fact]
    public void Cannot_go_backwards()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);
        election.MoveTo(ElectionStatus.VotingOpen, Now);

        election.MoveTo(ElectionStatus.NominationsOpen, Now).Should().BeFalse();
        election.Status.Should().Be(ElectionStatus.VotingOpen);
    }

    [Fact]
    public void An_election_cannot_be_moved_straight_to_Published()
    {
        // Publishing is its own command - it has to pick and freeze a winner.
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);
        election.MoveTo(ElectionStatus.VotingOpen, Now);
        election.MoveTo(ElectionStatus.Closed, Now);

        election.MoveTo(ElectionStatus.Published, Now).Should().BeFalse();
    }

    // ---- Nominating ---------------------------------------------------------

    [Fact]
    public void Nominating_requires_nominations_to_be_open()
    {
        var election = Election();

        election.Nominate(Guid.NewGuid(), PresidentId, Now).Should().BeNull();
    }

    [Fact]
    public void Nominating_the_same_member_twice_is_a_no_op()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);
        var memberId = Guid.NewGuid();

        election.Nominate(memberId, PresidentId, Now).Should().NotBeNull();
        election.Nominate(memberId, Guid.NewGuid(), Now).Should().BeNull();
        election.Candidates.Should().ContainSingle();
    }

    // ---- The ballot -----------------------------------------------------------

    [Fact]
    public void The_ballot_is_approved_candidates_only()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);

        var approved = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;
        var waiting = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;

        election.ApproveCandidate(approved.Id, PresidentId, Now);

        election.Ballot.Should().ContainSingle().Which.Id.Should().Be(approved.Id);
        election.FindCandidate(waiting.Id).Should().NotBeNull("a nomination is not a candidate until approved");
    }

    [Fact]
    public void Removing_a_candidate_is_refused_once_voting_has_opened()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);
        var candidate = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;
        election.ApproveCandidate(candidate.Id, PresidentId, Now);
        election.MoveTo(ElectionStatus.VotingOpen, Now);

        election.RejectCandidate(candidate.Id, Now).Should()
            .BeFalse("removing now would discard votes already cast for them");
    }

    // ---- Picking the winner -----------------------------------------------

    [Fact]
    public void The_highest_tally_wins()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);
        var leader = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;
        var runnerUp = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;
        election.ApproveCandidate(leader.Id, PresidentId, Now);
        election.ApproveCandidate(runnerUp.Id, PresidentId, Now);

        var tally = new Dictionary<Guid, int> { [leader.Id] = 5, [runnerUp.Id] = 3 };

        election.WinnersBy(tally).Should().ContainSingle().Which.Id.Should().Be(leader.Id);
    }

    [Fact]
    public void A_tie_is_broken_by_earliest_nomination()
    {
        // Created deliberately out of order, so this distinguishes "the
        // tie-break is working" from "candidates happened to already be in
        // the right order" - the same discipline
        // celebrity-voting-service's own CampaignMappingsTests uses for
        // RankBy.
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);

        var later = election.Nominate(Guid.NewGuid(), PresidentId, Now.AddHours(2))!;
        var earlier = election.Nominate(Guid.NewGuid(), PresidentId, Now.AddHours(1))!;
        election.ApproveCandidate(later.Id, PresidentId, Now);
        election.ApproveCandidate(earlier.Id, PresidentId, Now);

        var tally = new Dictionary<Guid, int> { [later.Id] = 2, [earlier.Id] = 2 };

        election.WinnersBy(tally).Should().ContainSingle().Which.Id.Should().Be(earlier.Id);
    }

    [Fact]
    public void No_candidates_means_no_winner()
    {
        Election().WinnersBy(new Dictionary<Guid, int>()).Should().BeEmpty();
    }

    [Fact]
    public void A_multi_seat_role_elects_the_top_candidates_up_to_its_seats()
    {
        var election = Election(seats: 2);
        election.MoveTo(ElectionStatus.NominationsOpen, Now);
        var first = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;
        var third = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;
        var second = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;

        foreach (var candidate in new[] { first, second, third })
        {
            election.ApproveCandidate(candidate.Id, PresidentId, Now);
        }

        var tally = new Dictionary<Guid, int> { [first.Id] = 9, [second.Id] = 6, [third.Id] = 2 };

        election.WinnersBy(tally).Select(c => c.Id).Should().Equal(first.Id, second.Id);
    }

    [Fact]
    public void A_ballot_shorter_than_the_seats_elects_everybody_on_it()
    {
        var election = Election(seats: 3);
        election.MoveTo(ElectionStatus.NominationsOpen, Now);
        var only = election.Nominate(Guid.NewGuid(), PresidentId, Now)!;
        election.ApproveCandidate(only.Id, PresidentId, Now);

        election.WinnersBy(new Dictionary<Guid, int>()).Should().ContainSingle().Which.Id.Should().Be(only.Id);
    }

    // ---- Publishing -----------------------------------------------------------

    [Fact]
    public void Publishing_requires_the_election_to_be_closed()
    {
        var election = Election();

        election.Publish([Guid.NewGuid()], Now).Should().BeFalse();
    }

    [Fact]
    public void Publishing_freezes_the_winner_and_cannot_happen_twice()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);
        election.MoveTo(ElectionStatus.VotingOpen, Now);
        election.MoveTo(ElectionStatus.Closed, Now);

        var winnerId = Guid.NewGuid();

        election.Publish([winnerId], Now).Should().BeTrue();
        election.Status.Should().Be(ElectionStatus.Published);
        election.WinnerMemberIds.Should().Equal(winnerId);
        election.PublishedAt.Should().Be(Now);

        election.Publish([Guid.NewGuid()], Now).Should()
            .BeFalse("a second publish would leave \"the result\" with no single referent");
        election.WinnerMemberIds.Should().Equal([winnerId], "the first result is the one that stands");
    }

    // ---- Correcting it, and reopening nominations that closed too early -------

    [Theory]
    [InlineData(ElectionStatus.Draft, true)]
    [InlineData(ElectionStatus.NominationsOpen, true)]
    [InlineData(ElectionStatus.VotingOpen, false)]
    [InlineData(ElectionStatus.Closed, false)]
    [InlineData(ElectionStatus.Published, false)]
    public void CanBeEdited_only_before_voting_opens(ElectionStatus status, bool expected)
    {
        var election = Election();

        if (status is ElectionStatus.NominationsOpen or ElectionStatus.VotingOpen
            or ElectionStatus.Closed or ElectionStatus.Published)
        {
            election.MoveTo(ElectionStatus.NominationsOpen, Now);
        }

        if (status is ElectionStatus.VotingOpen or ElectionStatus.Closed or ElectionStatus.Published)
        {
            election.MoveTo(ElectionStatus.VotingOpen, Now);
        }

        if (status is ElectionStatus.Closed or ElectionStatus.Published)
        {
            election.MoveTo(ElectionStatus.Closed, Now);
        }

        if (status is ElectionStatus.Published)
        {
            election.Publish([Guid.NewGuid()], Now);
        }

        election.CanBeEdited.Should().Be(expected);
    }

    [Fact]
    public void Reopening_extends_the_nomination_window_without_touching_status()
    {
        // Nominations closed by the clock: the status never advanced past
        // NominationsOpen, so AcceptsNominations is already false with nothing
        // to walk back - extending the date is the whole fix.
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Now);

        // Past the fixture's own nominationEndAt (Now + 7 days) - the window
        // has closed by the clock, with the status never having moved on.
        var closed = Now.AddDays(10);

        election.AcceptsNominations(closed).Should().BeFalse();

        election.Update(
            RoleId,
            "Secretary",
            1,
            nominationStartAt: Now,
            nominationEndAt: closed.AddDays(7),
            votingStartAt: closed.AddDays(7),
            votingEndAt: closed.AddDays(14),
            closed);

        election.Status.Should().Be(ElectionStatus.NominationsOpen, "reopening is not a status move");
        election.AcceptsNominations(closed).Should().BeTrue();
    }

    [Fact]
    public void Updating_trims_the_position_title_and_announces_it()
    {
        var election = Election();
        election.ClearDomainEvents();

        election.Update(
            RoleId, "  Assistant Secretary  ", 1, Now, Now.AddDays(7), Now.AddDays(7), Now.AddDays(14), Now);

        election.PositionTitle.Should().Be("Assistant Secretary");
        election.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<GroupElectionUpdatedDomainEvent>();
    }
}
