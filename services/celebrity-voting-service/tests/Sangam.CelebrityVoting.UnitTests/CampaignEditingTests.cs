using FluentAssertions;
using Sangam.CelebrityVoting.Domain.Campaigns;
using Xunit;

namespace Sangam.CelebrityVoting.UnitTests;

/// <summary>
/// Correcting a campaign's details, and the line past which it is refused.
/// </summary>
public sealed class CampaignEditingTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static VotingCampaign Campaign() =>
        VotingCampaign.Create(
            TenantId, "Celebrities 2026", null,
            Now, Now.AddDays(7), Now.AddDays(7), Now.AddDays(14),
            topN: 3, ResultsVisibility.Live, Now);

    [Fact]
    public void Updating_replaces_every_detail_it_is_given()
    {
        var campaign = Campaign();

        campaign.Update(
            "  Renamed  ", "  About it  ",
            Now.AddDays(1), Now.AddDays(8), Now.AddDays(9), Now.AddDays(16),
            topN: 5, ResultsVisibility.HiddenUntilClose, Now);

        campaign.Title.Should().Be("Renamed");
        campaign.Description.Should().Be("About it");
        campaign.NominationEndAt.Should().Be(Now.AddDays(8));
        campaign.VotingStartAt.Should().Be(Now.AddDays(9));
        campaign.TopN.Should().Be(5);
        campaign.ResultsVisibility.Should().Be(ResultsVisibility.HiddenUntilClose);
    }

    [Fact]
    public void Updating_announces_the_correction_without_carrying_any_of_it()
    {
        var campaign = Campaign();

        campaign.Update("Renamed", null, Now, Now.AddDays(7), Now.AddDays(7), Now.AddDays(14),
            3, ResultsVisibility.Live, Now);

        var raised = campaign.DomainEvents.Should().ContainSingle().Subject
            .Should().BeOfType<CampaignUpdatedDomainEvent>().Subject;

        raised.Topic.Should().Be("celebrity-voting.campaign.updated.v1");
        raised.CampaignId.Should().Be(campaign.Id);
    }

    [Fact]
    public void A_blank_title_is_refused_the_way_creation_refuses_it()
    {
        var update = () => Campaign().Update("  ", null, Now, Now.AddDays(7), Now.AddDays(7),
            Now.AddDays(14), 3, ResultsVisibility.Live, Now);

        update.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_draft_and_a_campaign_taking_nominations_can_be_edited()
    {
        var campaign = Campaign();
        campaign.CanBeEdited.Should().BeTrue();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);
        campaign.CanBeEdited.Should().BeTrue();
    }

    [Fact]
    public void Once_voting_opens_the_campaign_can_no_longer_be_edited()
    {
        // People have been told the windows and whether the tally is live, and
        // votes may exist. Moving either under them changes the election after
        // the fact.
        var campaign = Campaign();
        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);
        var candidate = campaign.Nominate(Guid.NewGuid(), "Education", Guid.NewGuid(), Now)!;
        campaign.ApproveCandidate(candidate.Id, Guid.NewGuid(), Now);
        campaign.MoveTo(CampaignStatus.VotingOpen, Now).Should().BeTrue();

        campaign.CanBeEdited.Should().BeFalse();

        campaign.MoveTo(CampaignStatus.Closed, Now);
        campaign.CanBeEdited.Should().BeFalse();
    }
}
