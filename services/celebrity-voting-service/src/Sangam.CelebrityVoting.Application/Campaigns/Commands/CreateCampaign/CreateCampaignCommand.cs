using FluentValidation;
using MediatR;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;
using Sangam.CelebrityVoting.Domain.Campaigns;

namespace Sangam.CelebrityVoting.Application.Campaigns.Commands.CreateCampaign;

/// <summary>
/// Sets a campaign up. It stays a draft until somebody opens nominations.
/// </summary>
[RequiresPermission(PermissionKeys.CelebrityVotingConfigure)]
public sealed record CreateCampaignCommand(
    string Title,
    string? Description,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt,
    int TopN,
    string ResultsVisibility) : ICommand<CampaignResponse>;

public sealed class CreateCampaignCommandValidator : AbstractValidator<CreateCampaignCommand>
{
    public CreateCampaignCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleFor(x => x.NominationEndAt)
            .GreaterThan(x => x.NominationStartAt)
            .WithMessage("Nominations cannot close before they open.");

        RuleFor(x => x.VotingEndAt)
            .GreaterThan(x => x.VotingStartAt)
            .WithMessage("Voting cannot close before it opens.");

        // Voting on a ballot that is still being nominated to means the people
        // who vote early see a different ballot from the people who vote late.
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

public sealed class CreateCampaignCommandHandler(
    ICampaignRepository campaigns,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateCampaignCommand, Result<CampaignResponse>>
{
    public async Task<Result<CampaignResponse>> Handle(
        CreateCampaignCommand command,
        CancellationToken cancellationToken)
    {
        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<CampaignResponse>(Error.Forbidden(
                "Campaign.NoSamaaj", "Select a Samaaj before creating a campaign in it."));
        }

        var campaign = VotingCampaign.Create(
            tenantId,
            command.Title,
            command.Description,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            command.TopN,
            Enum.Parse<ResultsVisibility>(command.ResultsVisibility, ignoreCase: true),
            clock.UtcNow);

        campaigns.Add(campaign);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(campaign.ToResponse(clock.UtcNow, myVoteCandidateId: null));
    }
}
