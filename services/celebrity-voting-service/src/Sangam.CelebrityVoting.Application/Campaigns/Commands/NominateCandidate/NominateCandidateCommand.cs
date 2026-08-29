using FluentValidation;
using MediatR;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;

namespace Sangam.CelebrityVoting.Application.Campaigns.Commands.NominateCandidate;

/// <summary>Puts a member forward. A reviewer decides whether they reach the ballot.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record NominateCandidateCommand(Guid CampaignId, Guid MemberId, string? Category)
    : ICommand<NominateResponse>;

/// <summary>
/// <paramref name="Nominated"/> is false when this member was already put
/// forward. Reported as success: the second nominator has done nothing wrong,
/// and one candidacy per member is what keeps a vote from splitting.
/// </summary>
/// <remarks>
/// <paramref name="CandidateId"/> is the candidacy this nomination resolved to,
/// which for a duplicate is the one that already existed. Returning it means
/// the second nominator learns <i>which</i> candidacy stands rather than only
/// that one does, and a caller never has to re-read the whole campaign to find
/// the row it just created.
/// </remarks>
public sealed record NominateResponse(
    Guid CampaignId, Guid CandidateId, Guid MemberId, bool Nominated);

public sealed class NominateCandidateCommandValidator
    : AbstractValidator<NominateCandidateCommand>
{
    public NominateCandidateCommandValidator()
    {
        RuleFor(x => x.CampaignId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
        RuleFor(x => x.Category).MaximumLength(100);
    }
}

public sealed class NominateCandidateCommandHandler(
    ICampaignRepository campaigns,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<NominateCandidateCommand, Result<NominateResponse>>
{
    public async Task<Result<NominateResponse>> Handle(
        NominateCandidateCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } nominatorId)
        {
            return Result.Failure<NominateResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var campaign = await campaigns.GetByIdAsync(command.CampaignId, cancellationToken);

        if (campaign is null
            || (tenantContext.TenantId is { } tenantId && campaign.TenantId != tenantId))
        {
            return Result.Failure<NominateResponse>(
                Error.NotFound("Campaign.NotFound", "No such campaign in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (!campaign.AcceptsNominations(now))
        {
            return Result.Failure<NominateResponse>(Error.Conflict(
                "Campaign.NominationsClosed", "Nominations for this campaign are not open."));
        }

        var candidate = campaign.Nominate(
            command.MemberId, command.Category, nominatorId, now);

        if (candidate is null)
        {
            // Already put forward. The candidacy that stands is the one to
            // report back, not an empty id.
            var existing = campaign.FindCandidateForMember(command.MemberId);

            return Result.Success(new NominateResponse(
                campaign.Id, existing?.Id ?? Guid.Empty, command.MemberId, false));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new NominateResponse(
            campaign.Id, candidate.Id, command.MemberId, true));
    }
}
