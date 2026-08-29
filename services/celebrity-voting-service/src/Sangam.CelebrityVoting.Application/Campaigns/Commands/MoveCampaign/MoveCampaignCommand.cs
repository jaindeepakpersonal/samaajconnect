using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;
using Sangam.CelebrityVoting.Domain.Campaigns;

namespace Sangam.CelebrityVoting.Application.Campaigns.Commands.MoveCampaign;

/// <summary>
/// Opens nominations, opens voting, or closes the campaign.
/// </summary>
/// <remarks>
/// Strictly forward: Draft → NominationsOpen → VotingOpen → Closed. An election
/// that can go backwards is not an election, so there is no route back and no
/// command to reopen one. Publishing the result is its own command, because it
/// has to compute and freeze the ranking.
/// </remarks>
[RequiresPermission(PermissionKeys.CelebrityVotingConfigure)]
public sealed record MoveCampaignCommand(Guid CampaignId, string Status)
    : ICommand<CampaignResponse>;

public sealed class MoveCampaignCommandValidator : AbstractValidator<MoveCampaignCommand>
{
    public MoveCampaignCommandValidator()
    {
        RuleFor(x => x.CampaignId).NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<CampaignStatus>(s, ignoreCase: true, out var parsed)
                && parsed != CampaignStatus.Published)
            .WithMessage(
                "Status must be NominationsOpen, VotingOpen or Closed. "
                + "Publishing the result is a separate call, because it freezes the ranking.");
    }
}

public sealed class MoveCampaignCommandHandler(
    ICampaignRepository campaigns,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<MoveCampaignCommandHandler> logger)
    : IRequestHandler<MoveCampaignCommand, Result<CampaignResponse>>
{
    public async Task<Result<CampaignResponse>> Handle(
        MoveCampaignCommand command,
        CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);

        if (campaign is null
            || (tenantContext.TenantId is { } tenantId && campaign.TenantId != tenantId))
        {
            return Result.Failure<CampaignResponse>(
                Error.NotFound("Campaign.NotFound", "No such campaign in this Samaaj."));
        }

        var target = Enum.Parse<CampaignStatus>(command.Status, ignoreCase: true);

        if (target == CampaignStatus.VotingOpen && campaign.Ballot.Count == 0)
        {
            // A ballot with nobody on it is a vote nobody can cast, and the
            // first anyone would learn of it is a member trying.
            return Result.Failure<CampaignResponse>(Error.Conflict(
                "Campaign.EmptyBallot",
                "No nominations have been approved, so there is nothing to vote on."));
        }

        if (!campaign.MoveTo(target, clock.UtcNow))
        {
            return Result.Failure<CampaignResponse>(Error.Conflict(
                "Campaign.InvalidTransition",
                $"A campaign that is {campaign.Status} cannot become {target}."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Campaign {CampaignId} moved to {Status}", campaign.Id, target);

        return Result.Success(campaign.ToResponse(clock.UtcNow, myVoteCandidateId: null));
    }
}
