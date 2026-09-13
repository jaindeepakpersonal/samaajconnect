using FluentValidation;
using MediatR;
using Sangam.VolunteerGroups.Application.Abstractions;
using Sangam.VolunteerGroups.Application.Common;
using Sangam.VolunteerGroups.Application.Security;

namespace Sangam.VolunteerGroups.Application.Elections.Commands.DecideCandidate;

/// <summary>
/// Puts a nomination on the ballot, or removes it before voting opens - the
/// president's decision, mirroring celebrity-voting-service's own
/// <c>DecideCandidateCommand</c> exactly.
/// </summary>
[RequiresPermission(PermissionKeys.VolunteerGroupsLead)]
public sealed record DecideCandidateCommand(Guid ElectionId, Guid CandidateId, bool Approve)
    : ICommand<ElectionDetailResponse>;

public sealed class DecideCandidateCommandValidator : AbstractValidator<DecideCandidateCommand>
{
    public DecideCandidateCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.CandidateId).NotEmpty();
    }
}

public sealed class DecideCandidateCommandHandler(
    IGroupRepository groups,
    IElectionRepository elections,
    IVoteRepository votes,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<DecideCandidateCommand, Result<ElectionDetailResponse>>
{
    public async Task<Result<ElectionDetailResponse>> Handle(
        DecideCandidateCommand command, CancellationToken cancellationToken)
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

        var group = await groups.GetByIdAsync(election.GroupId, cancellationToken);

        if (group is null || !group.IsPresident(actorId))
        {
            return Result.Failure<ElectionDetailResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        var now = clock.UtcNow;

        var changed = command.Approve
            ? election.ApproveCandidate(command.CandidateId, actorId, now)
            : election.RejectCandidate(command.CandidateId, now);

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

        var tally = await votes.TallyAsync(election.Id, cancellationToken);

        return Result.Success(election.ToDetail(now, myVoteCandidateId: null, tally));
    }
}
