using FluentValidation;
using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;

namespace Sangam.Panch.Application.Elections.Commands.DecideCandidate;

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
[RequiresPermission(PermissionKeys.PanchManage)]
public sealed record DecideCandidateCommand(Guid ElectionId, Guid CandidateId, bool Approve)
    : ICommand<ElectionDetailResponse>;

public sealed class DecideCandidateCommandValidator
    : AbstractValidator<DecideCandidateCommand>
{
    public DecideCandidateCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
    }
}

public sealed class DecideCandidateCommandHandler(
    IElectionRepository elections,
    IBallotRepository ballots,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<DecideCandidateCommand, Result<ElectionDetailResponse>>
{
    public async Task<Result<ElectionDetailResponse>> Handle(
        DecideCandidateCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<ElectionDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is { } tenantId && election.TenantId != tenantId))
        {
            return Result.Failure<ElectionDetailResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        var changed = command.Approve
            ? election.ApproveCandidate(command.CandidateId, actorId, now)
            : election.RejectCandidate(command.CandidateId, actorId, now);

        if (!changed && election.FindCandidate(command.CandidateId) is null)
        {
            return Result.Failure<ElectionDetailResponse>(
                Error.NotFound("Candidate.NotFound", "No such nomination in this election."));
        }

        if (!changed && !command.Approve)
        {
            return Result.Failure<ElectionDetailResponse>(Error.Conflict(
                "Election.BallotSet",
                "Voting has opened, so removing a candidate would discard votes already cast."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // An administrator always sees the tally, so it is always fetched here.
        var tally = await ballots.TallyAsync(election.Id, cancellationToken);

        return Result.Success(election.ToDetail(
            now, myChoices: null, canAdminister: true, tally));
    }
}
