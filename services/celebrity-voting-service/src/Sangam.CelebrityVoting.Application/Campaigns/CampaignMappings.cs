using Sangam.CelebrityVoting.Domain.Campaigns;

namespace Sangam.CelebrityVoting.Application.Campaigns;

/// <summary>
/// The one place a campaign becomes a response, so there is one place to check
/// that a hidden tally stays hidden.
/// </summary>
internal static class CampaignMappings
{
    public static CampaignResponse ToResponse(
        this VotingCampaign campaign, DateTimeOffset now, Guid? myVoteCandidateId) => new(
        campaign.Id,
        campaign.Title,
        campaign.Description,
        campaign.NominationStartAt,
        campaign.NominationEndAt,
        campaign.VotingStartAt,
        campaign.VotingEndAt,
        campaign.TopN,
        campaign.ResultsVisibility.ToString(),
        campaign.Status.ToString(),
        campaign.AcceptsNominations(now),
        campaign.AcceptsVotes(now),
        myVoteCandidateId,
        campaign.Candidates.Count,
        campaign.CreatedAt);

    /// <summary>
    /// The ballot, with counts only when this caller may see them.
    /// </summary>
    /// <remarks>
    /// <paramref name="tally"/> is passed in rather than fetched here, so a
    /// caller who may not see it never causes the query at all. Fetching and
    /// then discarding would work and would be one refactor away from leaking.
    /// </remarks>
    public static CampaignDetailResponse ToDetail(
        this VotingCampaign campaign,
        DateTimeOffset now,
        Guid? myVoteCandidateId,
        bool canAdminister,
        IReadOnlyDictionary<Guid, int>? tally)
    {
        var tallyVisible = campaign.TallyVisibleTo(canAdminister) && tally is not null;

        // An administrator sees every nomination, including ones nobody has
        // approved: approving them is their job. A member sees the ballot.
        var candidates = canAdminister
            ? campaign.Candidates.OrderBy(c => c.NominatedAt).ToList()
            : [.. campaign.Ballot];

        return new CampaignDetailResponse(
            campaign.ToResponse(now, myVoteCandidateId),
            [
                .. candidates.Select(c => new CandidateResponse(
                    c.Id,
                    c.MemberId,
                    c.Category,
                    c.Status.ToString(),
                    c.NominatedBy,

                    // Null, not zero: zero is a claim, and the wrong one.
                    tallyVisible ? tally!.GetValueOrDefault(c.Id) : null))
            ],
            tallyVisible);
    }

    /// <summary>
    /// Ranks the ballot by votes, best first.
    /// </summary>
    /// <remarks>
    /// Ties break on nomination order, which is arbitrary but stable — the
    /// alternative is a ranking that reshuffles between two reads of the same
    /// numbers. A Samaaj settling an actual tie should do it themselves rather
    /// than have this pick.
    /// </remarks>
    public static IReadOnlyList<Candidate> RankBy(
        this VotingCampaign campaign, IReadOnlyDictionary<Guid, int> tally) =>
        [.. campaign.Ballot
            .OrderByDescending(c => tally.GetValueOrDefault(c.Id))
            .ThenBy(c => c.NominatedAt)];
}
