using FluentValidation;
using MediatR;
using Sangam.CelebrityVoting.Application.Abstractions;
using Sangam.CelebrityVoting.Application.Common;
using Sangam.CelebrityVoting.Application.Security;

namespace Sangam.CelebrityVoting.Application.Campaigns.Commands.NominateCandidate;

/// <summary>Puts a member forward. A reviewer decides whether they reach the ballot.</summary>
/// <remarks>
/// <paramref name="MemberId"/> is a string, not a <see cref="Guid"/> - a
/// Guid-typed wire field crashes ASP.NET Core's own JSON model binding with an
/// unhandled 400 the moment the value cannot be parsed, before FluentValidation
/// ever runs. Validated by <see cref="NominateCandidateCommandValidator"/> and
/// parsed only in the handler, the same defense-in-depth every master-data id
/// on matrimony-service's <c>OptIntoMatrimonyCommand</c> already uses.
///
/// <paramref name="Category"/> is required as of 2026-09-12: a nomination with
/// no stated reason or speciality left a reviewer nothing to judge it by, and
/// the ballot itself shows "No category given" for every one of the pre-existing
/// nominations that predate this - the field itself did not change, only
/// whether a new nomination may omit it.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record NominateCandidateCommand(Guid CampaignId, string MemberId, string Category)
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
        RuleFor(x => x.MemberId).Must(BeAGuid).WithMessage("Choose a member to nominate.");
        RuleFor(x => x.Category)
            .NotEmpty()
            .WithMessage("A reason or speciality is required for every nomination.")
            .MaximumLength(100);
    }

    private static bool BeAGuid(string value) => Guid.TryParse(value, out _);
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

        // Guaranteed parseable by NominateCandidateCommandValidator.BeAGuid.
        var memberId = Guid.Parse(command.MemberId);

        var candidate = campaign.Nominate(memberId, command.Category, nominatorId, now);

        if (candidate is null)
        {
            // Already put forward. The candidacy that stands is the one to
            // report back, not an empty id.
            var existing = campaign.FindCandidateForMember(memberId);

            return Result.Success(new NominateResponse(
                campaign.Id, existing?.Id ?? Guid.Empty, memberId, false));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new NominateResponse(campaign.Id, candidate.Id, memberId, true));
    }
}
