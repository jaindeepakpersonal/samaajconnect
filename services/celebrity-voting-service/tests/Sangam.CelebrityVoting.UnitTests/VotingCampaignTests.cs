using FluentAssertions;
using Sangam.CelebrityVoting.Domain.Campaigns;
using Xunit;

namespace Sangam.CelebrityVoting.UnitTests;

/// <summary>
/// The rules that decide whether a result means anything.
/// </summary>
/// <remarks>
/// Everything here is about the campaign as a thing that runs over time. The
/// double-voting guarantee is not tested at this level and cannot be: it is a
/// database index, and it lives in VoteIndexTests where a real database can
/// refuse a real duplicate.
/// </remarks>
public sealed class VotingCampaignTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static VotingCampaign Campaign(
        ResultsVisibility visibility = ResultsVisibility.Live) =>
        VotingCampaign.Create(
            TenantId,
            "Celebrities of Samaaj 2026",
            description: null,
            nominationStartAt: Now,
            nominationEndAt: Now.AddDays(7),
            votingStartAt: Now.AddDays(7),
            votingEndAt: Now.AddDays(14),
            topN: 3,
            resultsVisibility: visibility,
            now: Now);

    /// <summary>A campaign with nominations open and one approved candidate.</summary>
    private static (VotingCampaign Campaign, Candidate Candidate) WithBallot(
        ResultsVisibility visibility = ResultsVisibility.Live)
    {
        var campaign = Campaign(visibility);

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var candidate = campaign.Nominate(
            Guid.NewGuid(), "Community service", Guid.NewGuid(), Now)!;

        campaign.ApproveCandidate(candidate.Id, Guid.NewGuid(), Now);

        return (campaign, candidate);
    }

    // ---- The window and the status are both required -----------------------

    [Fact]
    public void A_draft_campaign_takes_no_nominations_even_inside_its_window()
    {
        // The window has opened; nobody has opened the campaign.
        Campaign().AcceptsNominations(Now).Should().BeFalse();
    }

    [Fact]
    public void Nominations_stop_at_the_end_of_their_window_without_anyone_clicking_close()
    {
        var campaign = Campaign();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        campaign.AcceptsNominations(Now.AddDays(1)).Should().BeTrue();
        campaign.AcceptsNominations(Now.AddDays(8)).Should().BeFalse(
            "the Samaaj was told a closing date, and an administrator forgetting "
            + "to click Close does not move it");
    }

    [Fact]
    public void Voting_does_not_open_early_just_because_the_status_did()
    {
        var (campaign, _) = WithBallot();

        campaign.MoveTo(CampaignStatus.VotingOpen, Now);

        campaign.AcceptsVotes(Now.AddDays(1)).Should().BeFalse("the voting window has not started");
        campaign.AcceptsVotes(Now.AddDays(8)).Should().BeTrue();
        campaign.AcceptsVotes(Now.AddDays(15)).Should().BeFalse("the voting window has closed");
    }

    // ---- Strictly forward --------------------------------------------------

    [Theory]
    [InlineData(CampaignStatus.VotingOpen)]
    [InlineData(CampaignStatus.Closed)]
    [InlineData(CampaignStatus.Published)]
    public void A_draft_campaign_can_only_move_to_nominations(CampaignStatus target)
    {
        Campaign().MoveTo(target, Now).Should().BeFalse();
    }

    [Fact]
    public void A_closed_campaign_cannot_reopen()
    {
        var (campaign, _) = WithBallot();

        campaign.MoveTo(CampaignStatus.VotingOpen, Now);
        campaign.MoveTo(CampaignStatus.Closed, Now.AddDays(14));

        campaign.MoveTo(CampaignStatus.VotingOpen, Now.AddDays(14)).Should().BeFalse(
            "an election that can be reopened after the count is not an election");

        campaign.Status.Should().Be(CampaignStatus.Closed);
    }

    [Fact]
    public void Closing_records_when_and_announces_it()
    {
        var (campaign, _) = WithBallot();

        campaign.MoveTo(CampaignStatus.VotingOpen, Now);
        campaign.MoveTo(CampaignStatus.Closed, Now.AddDays(14));

        campaign.ClosedAt.Should().Be(Now.AddDays(14));
        campaign.DomainEvents.Should().ContainItemsAssignableTo<CampaignClosedDomainEvent>();
    }

    // ---- One candidacy per member ------------------------------------------

    [Fact]
    public void Nominating_the_same_member_twice_adds_one_candidate()
    {
        var campaign = Campaign();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var memberId = Guid.NewGuid();

        campaign.Nominate(memberId, null, Guid.NewGuid(), Now).Should().NotBeNull();
        campaign.Nominate(memberId, null, Guid.NewGuid(), Now).Should().BeNull(
            "two entries for one person split their vote, and the second nominator "
            + "has done nothing wrong");

        campaign.Candidates.Should().ContainSingle();
    }

    [Fact]
    public void A_nomination_is_not_on_the_ballot_until_it_is_approved()
    {
        var campaign = Campaign();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var candidate = campaign.Nominate(Guid.NewGuid(), null, Guid.NewGuid(), Now)!;

        campaign.Ballot.Should().BeEmpty(
            "anyone can put anyone forward; a Samaaj is not made to hold a public "
            + "vote about a person on that alone");

        campaign.ApproveCandidate(candidate.Id, Guid.NewGuid(), Now).Should().BeTrue();
        campaign.Ballot.Should().ContainSingle().Which.Id.Should().Be(candidate.Id);
    }

    [Fact]
    public void Approving_an_approved_candidate_changes_nothing()
    {
        var (campaign, candidate) = WithBallot();

        campaign.ApproveCandidate(candidate.Id, Guid.NewGuid(), Now).Should().BeFalse();
        campaign.Ballot.Should().ContainSingle();
    }

    [Fact]
    public void A_candidate_can_be_removed_while_the_ballot_is_still_being_set()
    {
        var (campaign, candidate) = WithBallot();

        campaign.RejectCandidate(candidate.Id, Now).Should().BeTrue();
        campaign.Ballot.Should().BeEmpty();
    }

    [Fact]
    public void A_candidate_cannot_be_removed_once_voting_has_opened()
    {
        var (campaign, candidate) = WithBallot();

        campaign.MoveTo(CampaignStatus.VotingOpen, Now);

        campaign.RejectCandidate(candidate.Id, Now).Should().BeFalse(
            "removing them now would discard votes already cast for them");

        campaign.Ballot.Should().ContainSingle();
    }

    // ---- Who may see the running count -------------------------------------

    [Fact]
    public void A_hidden_tally_stays_hidden_from_members_while_voting_is_open()
    {
        var (campaign, _) = WithBallot(ResultsVisibility.HiddenUntilClose);

        campaign.MoveTo(CampaignStatus.VotingOpen, Now);

        campaign.TallyVisibleTo(canAdminister: false).Should().BeFalse(
            "the setting exists because members who can see who is winning vote "
            + "differently from members who cannot");
    }

    [Fact]
    public void A_live_tally_is_visible_to_members_while_voting_is_open()
    {
        var (campaign, _) = WithBallot();

        campaign.MoveTo(CampaignStatus.VotingOpen, Now);

        campaign.TallyVisibleTo(canAdminister: false).Should().BeTrue();
    }

    [Fact]
    public void A_hidden_tally_becomes_visible_once_voting_closes()
    {
        var (campaign, _) = WithBallot(ResultsVisibility.HiddenUntilClose);

        campaign.MoveTo(CampaignStatus.VotingOpen, Now);

        campaign.TallyVisibleTo(canAdminister: false).Should().BeFalse();

        campaign.MoveTo(CampaignStatus.Closed, Now.AddDays(14));

        campaign.TallyVisibleTo(canAdminister: false).Should().BeTrue();
    }

    [Fact]
    public void An_administrator_sees_a_hidden_tally_throughout()
    {
        var campaign = Campaign(ResultsVisibility.HiddenUntilClose);

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        campaign.TallyVisibleTo(canAdminister: true).Should().BeTrue(
            "somebody has to be able to tell whether the thing is working");
    }
}
