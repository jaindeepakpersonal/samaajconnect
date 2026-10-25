using FluentValidation;
using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;

namespace Sangam.Panch.Application.Elections.Commands.DeleteElection;

/// <summary>
/// Deletes an election that voting has not reached - a draft, or one taking
/// nominations - along with any nominations it holds.
/// </summary>
/// <remarks>
/// Refused from voting onward: by then members have voted, and a ballot is a
/// record of how the Samaaj voted that nobody should be able to make vanish.
/// Such an election is closed instead. Deleting one that is already gone is
/// 404, not success - unlike a repeated vote, there is no state to report.
/// </remarks>
[RequiresPermission(PermissionKeys.PanchManage)]
public sealed record DeleteElectionCommand(Guid ElectionId) : ICommand<DeletedElectionResponse>;

public sealed record DeletedElectionResponse(Guid Id);

public sealed class DeleteElectionCommandValidator : AbstractValidator<DeleteElectionCommand>
{
    public DeleteElectionCommandValidator() => RuleFor(x => x.ElectionId).NotEmpty();
}

public sealed class DeleteElectionCommandHandler(
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<DeleteElectionCommand, Result<DeletedElectionResponse>>
{
    public async Task<Result<DeletedElectionResponse>> Handle(
        DeleteElectionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<DeletedElectionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is { } tenantId && election.TenantId != tenantId))
        {
            return Result.Failure<DeletedElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        if (!election.CanBeDeleted)
        {
            return Result.Failure<DeletedElectionResponse>(Error.Conflict(
                "Election.NotDeletable",
                "Voting has opened, so this election holds ballots and cannot be deleted. Close it instead."));
        }

        // Raised before the row goes, and drained in the same save: the event
        // is the only record left that this election existed.
        election.MarkDeleted(actorId, clock.UtcNow);
        elections.Remove(election);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeletedElectionResponse(election.Id));
    }
}
