using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;
using Sangam.CelebrityVoting.Domain.Campaigns;

namespace Sangam.CelebrityVoting.Application.Campaigns.Commands.PublishResults;

/// <summary>
/// Computes the ranking and freezes it.
/// </summary>
/// <remarks>
/// Its own command rather than another status move, because it is the only one
/// that produces something: a <see cref="CampaignResult"/> holding the order,
/// stored once. A result recomputed on every read could change after it was
/// announced — by a correction, a removed candidate, a vote arriving late from
/// a retry — and an announced result that moves is worse than no result.
///
/// Only from <see cref="CampaignStatus.Closed"/>. Publishing a ranking while
/// votes are still arriving would announce an answer to a question still being
/// asked.
/// </remarks>
[RequiresPermission(PermissionKeys.CelebrityVotingConfigure)]
public sealed record PublishResultsCommand(Guid CampaignId) : ICommand<CampaignResultResponse>;

public sealed class PublishResultsCommandValidator : AbstractValidator<PublishResultsCommand>
{
    public PublishResultsCommandValidator() => RuleFor(x => x.CampaignId).NotEmpty();
}

public sealed class PublishResultsCommandHandler(
    ICampaignRepository campaigns,
    IVoteRepository votes,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<PublishResultsCommandHandler> logger)
    : IRequestHandler<PublishResultsCommand, Result<CampaignResultResponse>>
{
    public async Task<Result<CampaignResultResponse>> Handle(
        PublishResultsCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<CampaignResultResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);

        if (campaign is null
            || (tenantContext.TenantId is { } tenantId && campaign.TenantId != tenantId))
        {
            return Result.Failure<CampaignResultResponse>(
                Error.NotFound("Campaign.NotFound", "No such campaign in this Samaaj."));
        }

        if (campaign.Status == CampaignStatus.Published)
        {
            // Already announced. Returning the stored result rather than
            // recomputing is the whole point of storing it.
            var already = await votes.FindResultAsync(campaign.Id, cancellationToken);

            if (already is not null)
            {
                return Result.Success(await DescribeAsync(campaign, already, cancellationToken));
            }
        }

        var now = clock.UtcNow;

        if (!campaign.MoveTo(CampaignStatus.Published, now))
        {
            return Result.Failure<CampaignResultResponse>(Error.Conflict(
                "Campaign.NotClosed",
                "Close the campaign before publishing its result. Publishing while votes are "
                + "still arriving would announce an answer to a question still being asked."));
        }

        var tally = await votes.TallyAsync(campaign.Id, cancellationToken);

        var ranked = campaign.RankBy(tally).Take(campaign.TopN).Select(c => c.Id).ToList();

        var result = new CampaignResult(campaign.Id, ranked, actorId, now);

        votes.AddResult(result);
        campaign.AnnounceResults(ranked, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Campaign {CampaignId} published with {Count} place(s)", campaign.Id, ranked.Count);

        return Result.Success(await DescribeAsync(campaign, result, cancellationToken));
    }

    private async Task<CampaignResultResponse> DescribeAsync(
        VotingCampaign campaign, CampaignResult result, CancellationToken cancellationToken)
    {
        var tally = await votes.TallyAsync(campaign.Id, cancellationToken);

        return new CampaignResultResponse(
            campaign.Id,
            [
                .. result.RankedCandidateIds.Select((candidateId, index) => new ResultEntry(
                    index + 1,
                    candidateId,
                    campaign.FindCandidate(candidateId)?.MemberId ?? Guid.Empty,
                    tally.GetValueOrDefault(candidateId)))
            ],
            result.PublishedBy,
            result.PublishedAt);
    }
}
