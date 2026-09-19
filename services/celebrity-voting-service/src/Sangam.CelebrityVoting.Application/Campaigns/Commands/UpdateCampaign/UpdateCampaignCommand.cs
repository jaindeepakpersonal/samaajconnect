using FluentValidation;
using MediatR;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;
using Sangam.CelebrityVoting.Domain.Campaigns;

namespace Sangam.CelebrityVoting.Application.Campaigns.Commands.UpdateCampaign;

/// <summary>
/// Corrects a campaign's details. Allowed while it is a draft or taking
/// nominations, refused from voting onward.
/// </summary>
[RequiresPermission(PermissionKeys.CelebrityVotingConfigure)]
public sealed record UpdateCampaignCommand(
    Guid CampaignId,
    string Title,
    string? Description,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt,
    int TopN,
    string ResultsVisibility) : ICommand<CampaignResponse>;

/// <summary>The same rules as creating one, so an edit cannot reach a state a create could not.</summary>
public sealed class UpdateCampaignCommandValidator : AbstractValidator<UpdateCampaignCommand>
{
    public UpdateCampaignCommandValidator()
    {
        RuleFor(x => x.CampaignId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleFor(x => x.NominationEndAt)
            .GreaterThan(x => x.NominationStartAt)
            .WithMessage("Nominations cannot close before they open.");

        RuleFor(x => x.VotingEndAt)
            .GreaterThan(x => x.VotingStartAt)
            .WithMessage("Voting cannot close before it opens.");

        RuleFor(x => x.VotingStartAt)
            .GreaterThanOrEqualTo(x => x.NominationEndAt)
            .WithMessage("Voting must start after nominations close.");

        RuleFor(x => x.TopN)
            .InclusiveBetween(1, 100)
            .WithMessage("The result must have between 1 and 100 places.");

        RuleFor(x => x.ResultsVisibility)
            .NotEmpty()
            .Must(v => Enum.TryParse<ResultsVisibility>(v, ignoreCase: true, out _))
            .WithMessage("Results visibility must be Live or HiddenUntilClose.");
    }
}

public sealed class UpdateCampaignCommandHandler(
    ICampaignRepository campaigns,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateCampaignCommand, Result<CampaignResponse>>
{
    public async Task<Result<CampaignResponse>> Handle(
        UpdateCampaignCommand command,
        CancellationToken cancellationToken)
    {
        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);

        // IDOR guard on the write path, re-checked rather than left to the query filter.
        if (campaign is null
            || (tenantContext.TenantId is { } tenantId && campaign.TenantId != tenantId))
        {
            return Result.Failure<CampaignResponse>(
                Error.NotFound("Campaign.NotFound", "No such campaign in this Samaaj."));
        }

        if (!campaign.CanBeEdited)
        {
            return Result.Failure<CampaignResponse>(Error.Conflict(
                "Campaign.NotEditable",
                "Voting has opened, so the campaign can no longer be changed - "
                + "people have been told its windows and whether the tally is live."));
        }

        var now = clock.UtcNow;

        campaign.Update(
            command.Title,
            command.Description,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            command.TopN,
            Enum.Parse<ResultsVisibility>(command.ResultsVisibility, ignoreCase: true),
            now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(campaign.ToResponse(now, myVoteCandidateId: null));
    }
}
