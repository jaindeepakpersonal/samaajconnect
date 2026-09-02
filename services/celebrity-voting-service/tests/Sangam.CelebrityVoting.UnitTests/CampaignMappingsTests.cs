using FluentAssertions;
using Sangam.CelebrityVoting.Application.Campaigns;
using Sangam.CelebrityVoting.Domain.Campaigns;
using Xunit;

namespace Sangam.CelebrityVoting.UnitTests;

/// <summary>
/// Ranking a ballot, and deciding what a caller is allowed to see of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>`RankBy` is the function that decides who is named the celebrity of a
/// Samaaj, and it had no tests.</b> The aggregate's own rules were covered - the
/// windows, the forward-only sequence, who may see a tally - but the ordering
/// itself lives in an extension method in the application layer, which nothing
/// exercised. `PublishResultsCommand` takes its output, truncates it to `TopN`
/// and freezes it forever.
/// </para>
/// <para>
/// Everything here is a pure function over an aggregate, so this is the right
/// level: no database can make the ordering more or less correct.
/// </para>
/// </remarks>
public sealed class CampaignMappingsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static VotingCampaign Campaign(
        ResultsVisibility visibility = ResultsVisibility.Live, int topN = 3) =>
        VotingCampaign.Create(
            TenantId,
            "Celebrities of Samaaj 2026",
            description: null,
            nominationStartAt: Now,
            nominationEndAt: Now.AddDays(7),
            votingStartAt: Now.AddDays(7),
            votingEndAt: Now.AddDays(14),
            topN: topN,
            resultsVisibility: visibility,
            now: Now);

    /// <summary>Nominates somebody and puts them on the ballot.</summary>
    private static Candidate Approved(VotingCampaign campaign, DateTimeOffset nominatedAt)
    {
        var candidate = campaign.Nominate(Guid.NewGuid(), null, Guid.NewGuid(), nominatedAt)!;

        campaign.ApproveCandidate(candidate.Id, Guid.NewGuid(), nominatedAt);

        return candidate;
    }

    // ---- Ranking -----------------------------------------------------------

    [Fact]
    public void Ranks_the_most_voted_first()
    {
        var campaign = Campaign();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var few = Approved(campaign, Now);
        var many = Approved(campaign, Now.AddMinutes(1));
        var some = Approved(campaign, Now.AddMinutes(2));

        var ranked = campaign.RankBy(new Dictionary<Guid, int>
        {
            [few.Id] = 1,
            [many.Id] = 9,
            [some.Id] = 4,
        });

        ranked.Select(c => c.Id).Should().Equal(many.Id, some.Id, few.Id);
    }

    [Fact]
    public void Breaks_a_tie_on_nomination_order_and_does_not_reshuffle()
    {
        // Arbitrary but stable. The alternative is a ranking that comes out
        // differently on two reads of the same numbers, which for an announced
        // result is worse than a tie-break somebody disagrees with. A Samaaj
        // settling a real tie should do it themselves rather than have this
        // pick for them.
        //
        // The candidates are nominated *out of* chronological order on purpose.
        // Two things produce this ordering and they agree: `Ballot` sorts by
        // `NominatedAt`, and `RankBy` adds `ThenBy(NominatedAt)` on top of a
        // sort that is already stable. Removing either one alone therefore
        // changes nothing, and a test built on candidates created in order
        // cannot tell any of that apart - it passes whatever you delete.
        // Creating them out of order at least distinguishes "one of the two is
        // doing its job" from "neither is".
        var campaign = Campaign();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var last = Approved(campaign, Now.AddMinutes(2));
        var first = Approved(campaign, Now);
        var middle = Approved(campaign, Now.AddMinutes(1));

        var tally = new Dictionary<Guid, int>
        {
            [first.Id] = 5,
            [middle.Id] = 5,
            [last.Id] = 5,
        };

        var once = campaign.RankBy(tally).Select(c => c.Id).ToList();
        var twice = campaign.RankBy(tally).Select(c => c.Id).ToList();

        once.Should().Equal(first.Id, middle.Id, last.Id);
        twice.Should().Equal(once);
    }

    [Fact]
    public void Keeps_a_candidate_nobody_voted_for_at_the_bottom()
    {
        // Absent from the tally is zero votes, not absent from the ranking:
        // somebody who was on the ballot and got nothing is part of the result,
        // and dropping them would make the Top N quietly shorter.
        var campaign = Campaign();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var voted = Approved(campaign, Now);
        var unvoted = Approved(campaign, Now.AddMinutes(1));

        var ranked = campaign.RankBy(new Dictionary<Guid, int> { [voted.Id] = 2 });

        ranked.Select(c => c.Id).Should().Equal(voted.Id, unvoted.Id);
    }

    [Fact]
    public void Ranks_the_ballot_and_not_the_nominations()
    {
        // A nomination nobody approved is not a candidate. Ranking one would put
        // somebody in the result whom an administrator deliberately left off.
        var campaign = Campaign();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var onBallot = Approved(campaign, Now);
        var stillNominated = campaign.Nominate(Guid.NewGuid(), null, Guid.NewGuid(), Now)!;

        var ranked = campaign.RankBy(new Dictionary<Guid, int>
        {
            [onBallot.Id] = 1,

            // Even with votes against them, which cannot happen through the
            // API and would be the worst case if it ever did.
            [stillNominated.Id] = 99,
        });

        ranked.Select(c => c.Id).Should().Equal(onBallot.Id);
    }

    [Fact]
    public void Ranks_an_empty_ballot_as_nothing_rather_than_failing()
    {
        var campaign = Campaign();

        campaign.RankBy(new Dictionary<Guid, int>()).Should().BeEmpty();
    }

    [Fact]
    public void Ranks_everybody_and_leaves_the_truncation_to_the_caller()
    {
        // `PublishResultsCommand` does `RankBy(tally).Take(campaign.TopN)`.
        // Truncating here as well would mean the Top N was applied twice, which
        // is harmless - and truncating here *instead* would silently change what
        // the tally endpoint shows an administrator while voting is still open.
        var campaign = Campaign(topN: 2);

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var a = Approved(campaign, Now);
        var b = Approved(campaign, Now.AddMinutes(1));
        var c = Approved(campaign, Now.AddMinutes(2));

        var ranked = campaign.RankBy(new Dictionary<Guid, int>
        {
            [a.Id] = 3,
            [b.Id] = 2,
            [c.Id] = 1,
        });

        ranked.Should().HaveCount(3);
        ranked.Take(campaign.TopN).Select(x => x.Id).Should().Equal(a.Id, b.Id);
    }

    // ---- What a caller is shown --------------------------------------------

    [Fact]
    public void Shows_a_member_no_counts_at_all_on_a_hidden_campaign()
    {
        // Null rather than zero, because zero is a claim and the wrong one. The
        // admin panel draws "Not visible" off exactly this distinction, and a
        // member portal that read zero would tell somebody their candidate had
        // no votes.
        var campaign = Campaign(ResultsVisibility.HiddenUntilClose);

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var candidate = Approved(campaign, Now);

        campaign.MoveTo(CampaignStatus.VotingOpen, Now.AddDays(7));

        var detail = campaign.ToDetail(
            Now.AddDays(8), myVoteCandidateId: null, canAdminister: false, tally: null);

        detail.TallyVisible.Should().BeFalse();
        detail.Candidates.Should().ContainSingle(c => c.Id == candidate.Id);
        detail.Candidates.Single().Votes.Should().BeNull();
    }

    [Fact]
    public void Shows_an_administrator_the_counts_throughout()
    {
        // Somebody has to be able to tell whether the thing is working.
        var campaign = Campaign(ResultsVisibility.HiddenUntilClose);

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var candidate = Approved(campaign, Now);

        campaign.MoveTo(CampaignStatus.VotingOpen, Now.AddDays(7));

        var detail = campaign.ToDetail(
            Now.AddDays(8),
            myVoteCandidateId: null,
            canAdminister: true,
            tally: new Dictionary<Guid, int> { [candidate.Id] = 4 });

        detail.TallyVisible.Should().BeTrue();
        detail.Candidates.Single().Votes.Should().Be(4);
    }

    [Fact]
    public void Shows_a_visible_count_of_zero_as_zero()
    {
        // The other half of null-versus-zero: once a caller may see the tally, a
        // candidate with no votes really does have none, and null would then be
        // the misleading answer.
        var campaign = Campaign();

        campaign.MoveTo(CampaignStatus.NominationsOpen, Now);

        var candidate = Approved(campaign, Now);

        var detail = campaign.ToDetail(
            Now,
            myVoteCandidateId: null,
            canAdminister: true,
            tally: new Dictionary<Guid, int>());

        detail.Candidates.Single(c => c.Id == candidate.Id).Votes.Should().Be(0);
    }
}
