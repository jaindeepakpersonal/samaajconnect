using MediatR;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;

namespace Sangam.CelebrityVoting.Application.Campaigns.Queries;

/// <summary>This Samaaj's campaigns, newest first.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListCampaignsQuery : IQuery<IReadOnlyList<CampaignResponse>>;

public sealed class ListCampaignsQueryHandler(
    ICampaignRepository campaigns,
    IVoteRepository votes,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ListCampaignsQuery, Result<IReadOnlyList<CampaignResponse>>>
{
    public async Task<Result<IReadOnlyList<CampaignResponse>>> Handle(
        ListCampaignsQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<CampaignResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await campaigns.ListAsync(cancellationToken);
        var now = clock.UtcNow;
        var results = new List<CampaignResponse>(found.Count);

        foreach (var campaign in found)
        {
            // One targeted lookup per campaign rather than loading any votes.
            // A Samaaj has a handful of campaigns and thousands of votes; this
            // is the way round that scales.
            var mine = await votes.FindForVoterAsync(campaign.Id, memberId, cancellationToken);

            results.Add(campaign.ToResponse(now, mine?.CandidateId));
        }

        return Result.Success<IReadOnlyList<CampaignResponse>>(results);
    }
}

/// <summary>
/// One campaign with its ballot, and the tally when this caller may see it.
/// </summary>
/// <remarks>
/// A campaign set to HiddenUntilClose shows a member the names and no numbers
/// until voting is over. An administrator sees the numbers throughout, because
/// somebody has to be able to tell whether the thing is working.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetCampaignQuery(Guid CampaignId) : IQuery<CampaignDetailResponse>;

public sealed class GetCampaignQueryHandler(
    ICampaignRepository campaigns,
    IVoteRepository votes,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<GetCampaignQuery, Result<CampaignDetailResponse>>
{
    public async Task<Result<CampaignDetailResponse>> Handle(
        GetCampaignQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<CampaignDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var campaign = await campaigns.GetByIdAsync(query.CampaignId, cancellationToken);

        if (campaign is null)
        {
            return Result.Failure<CampaignDetailResponse>(
                Error.NotFound("Campaign.NotFound", "No such campaign in this Samaaj."));
        }

        var canAdminister = currentUser.HasPermission(PermissionKeys.CelebrityVotingConfigure);

        // Fetched only when this caller may see it. Fetching and then
        // discarding would work, and would be one refactor away from leaking.
        var tally = campaign.TallyVisibleTo(canAdminister)
            ? await votes.TallyAsync(campaign.Id, cancellationToken)
            : null;

        var mine = await votes.FindForVoterAsync(campaign.Id, memberId, cancellationToken);

        return Result.Success(campaign.ToDetail(
            clock.UtcNow, mine?.CandidateId, canAdminister, tally));
    }
}

/// <summary>
/// The published result: the frozen order, with the counts behind it.
/// </summary>
/// <remarks>
/// Reads the stored <c>CampaignResult</c> rather than recomputing. See
/// PublishResultsCommand for why an announced result must not move.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetResultsQuery(Guid CampaignId) : IQuery<CampaignResultResponse>;

public sealed class GetResultsQueryHandler(
    ICampaignRepository campaigns, IVoteRepository votes)
    : IRequestHandler<GetResultsQuery, Result<CampaignResultResponse>>
{
    public async Task<Result<CampaignResultResponse>> Handle(
        GetResultsQuery query,
        CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetByIdAsync(query.CampaignId, cancellationToken);

        if (campaign is null)
        {
            return Result.Failure<CampaignResultResponse>(
                Error.NotFound("Campaign.NotFound", "No such campaign in this Samaaj."));
        }

        var result = await votes.FindResultAsync(campaign.Id, cancellationToken);

        if (result is null)
        {
            return Result.Failure<CampaignResultResponse>(Error.NotFound(
                "Campaign.NotPublished", "This campaign has no published result."));
        }

        var tally = await votes.TallyAsync(campaign.Id, cancellationToken);

        return Result.Success(new CampaignResultResponse(
            campaign.Id,
            [
                .. result.RankedCandidateIds.Select((candidateId, index) => new ResultEntry(
                    index + 1,
                    candidateId,
                    campaign.FindCandidate(candidateId)?.MemberId ?? Guid.Empty,
                    tally.GetValueOrDefault(candidateId)))
            ],
            result.PublishedBy,
            result.PublishedAt));
    }
}
