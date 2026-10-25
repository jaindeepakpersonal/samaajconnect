using FluentAssertions;
using Sangam.Panch.Domain.Elections;
using Sangam.Panch.Domain.Panel;
using Xunit;

namespace Sangam.Panch.UnitTests;

/// <summary>
/// The rules that decide who sits on the Samaaj Panch.
/// </summary>
public sealed class PanchElectionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();

    private static PanchElection Election() =>
        PanchElection.Create(
            TenantId, "Panch 2026", null,
            Now, Now.AddDays(7), Now.AddDays(7), Now.AddDays(14),
            ResultsVisibility.Live, Now);

    /// <summary>An election with nominations open and <paramref name="count"/> approved candidates.</summary>
    private static (PanchElection Election, List<Candidate> Candidates) WithBallot(int count)
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Admin, Now);

        var candidates = new List<Candidate>();

        for (var i = 0; i < count; i++)
        {
            // Each nominated a minute apart, so nomination order is well defined.
            var candidate = election.Nominate(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(i))!;
            election.ApproveCandidate(candidate.Id, Guid.NewGuid(), Now.AddMinutes(i));
            candidates.Add(candidate);
        }

        return (election, candidates);
    }

    [Fact]
    public void The_panch_is_ten_members()
    {
        PanchElection.SeatCount.Should().Be(10);
    }

    [Fact]
    public void The_status_only_moves_forward()
    {
        var election = Election();

        election.MoveTo(ElectionStatus.VotingOpen, Admin, Now).Should().BeFalse("nominations come first");
        election.MoveTo(ElectionStatus.NominationsOpen, Admin, Now).Should().BeTrue();
        election.MoveTo(ElectionStatus.Draft, Admin, Now).Should().BeFalse("an election cannot go backwards");
    }

    [Fact]
    public void It_can_be_edited_and_deleted_only_until_voting_opens()
    {
        var (election, _) = WithBallot(1);

        election.CanBeEdited.Should().BeTrue();
        election.CanBeDeleted.Should().BeTrue();

        election.MoveTo(ElectionStatus.VotingOpen, Admin, Now.AddDays(7)).Should().BeTrue();

        election.CanBeEdited.Should().BeFalse("members have been told the windows and may have voted");
        election.CanBeDeleted.Should().BeFalse("deleting would destroy ballots");
    }

    [Fact]
    public void Voting_is_only_ever_extended_later_and_only_while_it_is_open()
    {
        var (election, _) = WithBallot(1);
        var actor = Guid.NewGuid();

        election.ExtendVoting(Now.AddDays(30), actor, Now).Should().BeFalse("voting has not opened yet");

        election.MoveTo(ElectionStatus.VotingOpen, Admin, Now.AddDays(7));
        election.ClearDomainEvents();

        election.ExtendVoting(Now.AddDays(10), actor, Now.AddDays(8)).Should().BeFalse("earlier than announced");
        election.ExtendVoting(Now.AddDays(14), actor, Now.AddDays(8)).Should().BeFalse("the same close is no extension");
        election.VotingEndAt.Should().Be(Now.AddDays(14));
        election.DomainEvents.Should().BeEmpty();

        election.ExtendVoting(Now.AddDays(21), actor, Now.AddDays(8)).Should().BeTrue();

        election.VotingEndAt.Should().Be(Now.AddDays(21));
        election.DomainEvents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ElectionVotingExtendedDomainEvent(
                election.Id, TenantId, Now.AddDays(14), Now.AddDays(21), actor, Now.AddDays(8)));
    }

    [Fact]
    public void An_edit_replaces_the_windows_without_moving_the_status()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Admin, Now);
        election.ClearDomainEvents();

        election.Update(
            " Panch 2026 ", "  ", Now, Now.AddDays(10), Now.AddDays(10), Now.AddDays(20),
            ResultsVisibility.HiddenUntilClose, Guid.NewGuid(), Now);

        election.Title.Should().Be("Panch 2026");
        election.Description.Should().BeNull();
        election.NominationEndAt.Should().Be(Now.AddDays(10));
        election.ResultsVisibility.Should().Be(ResultsVisibility.HiddenUntilClose);
        election.Status.Should().Be(ElectionStatus.NominationsOpen);
        election.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ElectionUpdatedDomainEvent>();
    }

    [Fact]
    public void A_status_change_names_the_administrator_who_made_it()
    {
        var (election, _) = WithBallot(1);
        var closer = Guid.NewGuid();
        election.MoveTo(ElectionStatus.VotingOpen, Admin, Now.AddDays(7));
        election.ClearDomainEvents();

        election.MoveTo(ElectionStatus.Closed, closer, Now.AddDays(8)).Should().BeTrue();

        election.DomainEvents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ElectionStatusChangedDomainEvent(
                election.Id, TenantId, "VotingOpen", "Closed", closer, Now.AddDays(8)));
    }

    [Fact]
    public void Approving_and_turning_down_a_nomination_each_name_who_decided()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Admin, Now);
        var approved = election.Nominate(Guid.NewGuid(), Guid.NewGuid(), Now)!;
        var turnedDown = election.Nominate(Guid.NewGuid(), Guid.NewGuid(), Now)!;
        election.ClearDomainEvents();

        election.ApproveCandidate(approved.Id, Admin, Now).Should().BeTrue();
        election.RejectCandidate(turnedDown.Id, Admin, Now).Should().BeTrue();

        election.DomainEvents.Should().BeEquivalentTo(
            [
                new CandidateDecidedDomainEvent(
                    election.Id, TenantId, approved.Id, approved.MemberId, "Approved", Admin, Now),
                new CandidateDecidedDomainEvent(
                    election.Id, TenantId, turnedDown.Id, turnedDown.MemberId, "TurnedDown", Admin, Now),
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_decision_that_changes_nothing_records_nothing()
    {
        var (election, candidates) = WithBallot(1);
        election.ClearDomainEvents();

        election.ApproveCandidate(candidates[0].Id, Admin, Now).Should().BeFalse("already on the ballot");
        election.MoveTo(ElectionStatus.VotingOpen, Admin, Now.AddDays(7));
        election.ClearDomainEvents();
        election.RejectCandidate(candidates[0].Id, Admin, Now.AddDays(8)).Should().BeFalse("the ballot is set");

        election.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void One_candidacy_per_member_however_many_nominate_them()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Admin, Now);
        var member = Guid.NewGuid();

        election.Nominate(member, Guid.NewGuid(), Now).Should().NotBeNull();
        election.Nominate(member, Guid.NewGuid(), Now).Should().BeNull();

        election.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void An_unapproved_nomination_is_not_on_the_ballot()
    {
        var election = Election();
        election.MoveTo(ElectionStatus.NominationsOpen, Admin, Now);
        election.Nominate(Guid.NewGuid(), Guid.NewGuid(), Now);

        election.Ballot.Should().BeEmpty();
    }

    [Fact]
    public void Winners_are_capped_at_ten_and_ranked_by_votes()
    {
        var (election, candidates) = WithBallot(12);
        var tally = candidates.Select((c, i) => (c, votes: i + 1)).ToDictionary(x => x.c.Id, x => x.votes);

        var winners = election.Winners(tally);

        winners.Should().HaveCount(10);
        winners[0].Id.Should().Be(candidates[11].Id, "the most votes ranks first");
        winners.Select(w => w.Id).Should().NotContain(candidates[0].Id, "fewest votes misses the cut");
        winners.Select(w => w.Id).Should().NotContain(candidates[1].Id);
    }

    [Fact]
    public void A_tie_is_broken_by_earliest_nomination_in_a_stable_order()
    {
        var (election, candidates) = WithBallot(3);
        var tally = candidates.ToDictionary(c => c.Id, _ => 5);

        election.Winners(tally).Select(c => c.Id)
            .Should().Equal(candidates.Select(c => c.Id), "equal votes fall back to who was nominated first");
    }

    [Fact]
    public void A_candidate_nobody_voted_for_does_not_win_a_seat()
    {
        var (election, candidates) = WithBallot(3);
        var tally = new Dictionary<Guid, int> { [candidates[2].Id] = 4 };

        election.Winners(tally).Should().ContainSingle().Which.Id.Should().Be(candidates[2].Id);
    }

    [Fact]
    public void Publishing_announces_the_roster_as_a_snapshot_of_ids()
    {
        var (election, candidates) = WithBallot(2);
        var seated = candidates.Select(c => c.MemberId).ToList();
        election.ClearDomainEvents();

        election.AnnouncePanel(seated, Guid.NewGuid(), Now);

        var raised = election.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<PanelChangedDomainEvent>().Subject;

        raised.Topic.Should().Be("panch.panel.changed.v1");
        raised.MemberIds.Should().Equal(seated);
        raised.Reason.Should().Be("Elected");
    }

    [Fact]
    public void Removing_a_member_announces_who_is_left()
    {
        var leaving = new PanelMember(TenantId, Guid.NewGuid(), Guid.NewGuid(), Now);
        var stays = Guid.NewGuid();

        leaving.AnnounceRemoval([stays], Guid.NewGuid(), Now);

        var raised = leaving.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<PanelChangedDomainEvent>().Subject;

        raised.MemberIds.Should().Equal(stays);
        raised.Reason.Should().Be("Removed");
    }

    [Fact]
    public void A_hidden_tally_stays_hidden_from_a_member_until_voting_is_over()
    {
        var election = PanchElection.Create(
            TenantId, "Hidden", null, Now, Now.AddDays(7), Now.AddDays(7), Now.AddDays(14),
            ResultsVisibility.HiddenUntilClose, Now);

        election.TallyVisibleTo(canAdminister: false).Should().BeFalse();
        election.TallyVisibleTo(canAdminister: true).Should().BeTrue("somebody has to see whether it works");
    }
}
