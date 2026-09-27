using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Elections.Commands.DecideTrustCandidate;

/// <summary>
/// Puts a nomination on the ballot, or removes it before voting opens -
/// mirroring volunteer-groups-service's own <c>DecideCandidateCommand</c>
/// exactly.
/// </summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record DecideTrustCandidateCommand(Guid ElectionId, Guid CandidateId, bool Approve)
    : ICommand<TrustElectionDetailResponse>;

public sealed class DecideTrustCandidateCommandValidator : AbstractValidator<DecideTrustCandidateCommand>
{
    public DecideTrustCandidateCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
    }
}

public sealed class DecideTrustCandidateCommandHandler(
    ITrustRepository trusts,
    IElectionRepository elections,
    IVoteRepository votes,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<DecideTrustCandidateCommand, Result<TrustElectionDetailResponse>>
{
    public async Task<Result<TrustElectionDetailResponse>> Handle(
        DecideTrustCandidateCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<TrustElectionDetailResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);
        var trust = election is null ? null : await trusts.GetByIdAsync(election.TrustId, cancellationToken);

        if (election is null || trust is null)
        {
            return Result.Failure<TrustElectionDetailResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        var changed = command.Approve
            ? election.ApproveCandidate(command.CandidateId, actorId, now)
            : election.RejectCandidate(command.CandidateId, now);

        if (!changed && election.FindCandidate(command.CandidateId) is null)
        {
            return Result.Failure<TrustElectionDetailResponse>(
                Error.NotFound("Candidate.NotFound", "No such nomination in this election."));
        }

        if (!changed && !command.Approve)
        {
            return Result.Failure<TrustElectionDetailResponse>(Error.Conflict(
                "Election.BallotSet",
                "Voting has opened, so removing a candidate would discard votes already cast."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tally = await votes.TallyAsync(election.Id, cancellationToken);

        return Result.Success(election.ToDetail(trust, now, [], tally));
    }
}
