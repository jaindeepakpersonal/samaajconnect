using FluentValidation;
using MediatR;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;

namespace Sangam.CelebrityVoting.Application.Campaigns.Commands.DecideCandidate;

/// <summary>
/// Puts a nomination on the ballot, or removes it.
/// </summary>
/// <remarks>
/// Nominations are approved rather than going straight to the ballot, for the
/// same reason timeline posts are moderated: anyone can put anyone's name
/// forward, and a Samaaj should not be made to hold a public popularity vote
/// about a person because one member typed their name.
///
/// Removing works only before voting opens. After that the ballot is set, and
/// removing a candidate would discard votes already cast for them.
/// </remarks>
[RequiresPermission(PermissionKeys.CelebrityVotingConfigure)]
public sealed record DecideCandidateCommand(Guid CampaignId, Guid CandidateId, bool Approve)
    : ICommand<CampaignDetailResponse>;

public sealed class DecideCandidateCommandValidator
    : AbstractValidator<DecideCandidateCommand>
{
    public DecideCandidateCommandValidator()
    {
        RuleFor(x => x.CampaignId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
    }
}

public sealed class DecideCandidateCommandHandler(
    ICampaignRepository campaigns,
    IVoteRepository votes,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<DecideCandidateCommand, Result<CampaignDetailResponse>>
{
    public async Task<Result<CampaignDetailResponse>> Handle(
        DecideCandidateCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<CampaignDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);

        if (campaign is null
            || (tenantContext.TenantId is { } tenantId && campaign.TenantId != tenantId))
        {
            return Result.Failure<CampaignDetailResponse>(
                Error.NotFound("Campaign.NotFound", "No such campaign in this Samaaj."));
        }

        var now = clock.UtcNow;

        var changed = command.Approve
            ? campaign.ApproveCandidate(command.CandidateId, actorId, now)
            : campaign.RejectCandidate(command.CandidateId, now);

        if (!changed && campaign.FindCandidate(command.CandidateId) is null)
        {
            return Result.Failure<CampaignDetailResponse>(
                Error.NotFound("Candidate.NotFound", "No such nomination in this campaign."));
        }

        if (!changed && !command.Approve)
        {
            return Result.Failure<CampaignDetailResponse>(Error.Conflict(
                "Campaign.BallotSet",
                "Voting has opened, so removing a candidate would discard votes already cast."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // An administrator always sees the tally, so it is always fetched here.
        var tally = await votes.TallyAsync(campaign.Id, cancellationToken);

        return Result.Success(campaign.ToDetail(
            now, myVoteCandidateId: null, canAdminister: true, tally));
    }
}
