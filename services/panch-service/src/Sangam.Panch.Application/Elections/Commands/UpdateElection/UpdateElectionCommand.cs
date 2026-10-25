using FluentValidation;
using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;
using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Application.Elections.Commands.UpdateElection;

/// <summary>
/// Corrects an election before voting opens: its title, description, both
/// windows and results visibility. Extending nominations is this - pushing
/// their close date out.
/// </summary>
/// <remarks>
/// The whole election is replaced, the same shape as creating one, and the
/// window rules are the same rules. Once voting has opened only the voting
/// close may move, and only later - <c>ExtendVotingCommand</c>.
/// </remarks>
[RequiresPermission(PermissionKeys.PanchManage)]
public sealed record UpdateElectionCommand(
    Guid ElectionId,
    string Title,
    string? Description,
    DateTimeOffset NominationStartAt,
    DateTimeOffset NominationEndAt,
    DateTimeOffset VotingStartAt,
    DateTimeOffset VotingEndAt,
    string ResultsVisibility) : ICommand<ElectionResponse>;

public sealed class UpdateElectionCommandValidator : AbstractValidator<UpdateElectionCommand>
{
    public UpdateElectionCommandValidator()
    {
        RuleFor(x => x.ElectionId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);

        RuleFor(x => x.NominationEndAt)
            .GreaterThan(x => x.NominationStartAt)
            .WithMessage("Nominations cannot close before they open.");

        RuleFor(x => x.VotingEndAt)
            .GreaterThan(x => x.VotingStartAt)
            .WithMessage("Voting cannot close before it opens.");

        // CreateElectionCommandValidator's rule, for its reason: a ballot still
        // being nominated to is a different ballot for early and late voters.
        RuleFor(x => x.VotingStartAt)
            .GreaterThanOrEqualTo(x => x.NominationEndAt)
            .WithMessage("Voting must start after nominations close.");

        RuleFor(x => x.ResultsVisibility)
            .NotEmpty()
            .Must(v => Enum.TryParse<ResultsVisibility>(v, ignoreCase: true, out _))
            .WithMessage("Results visibility must be Live or HiddenUntilClose.");
    }
}

public sealed class UpdateElectionCommandHandler(
    IElectionRepository elections,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateElectionCommand, Result<ElectionResponse>>
{
    public async Task<Result<ElectionResponse>> Handle(
        UpdateElectionCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<ElectionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is { } tenantId && election.TenantId != tenantId))
        {
            return Result.Failure<ElectionResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        if (!election.CanBeEdited)
        {
            return Result.Failure<ElectionResponse>(Error.Conflict(
                "Election.NotEditable",
                election.Status == ElectionStatus.VotingOpen
                    ? "Voting has opened, so only the voting close date can change, and only to later."
                    : $"An election that is {election.Status} can no longer be changed."));
        }

        election.Update(
            command.Title,
            command.Description,
            command.NominationStartAt,
            command.NominationEndAt,
            command.VotingStartAt,
            command.VotingEndAt,
            Enum.Parse<ResultsVisibility>(command.ResultsVisibility, ignoreCase: true),
            actorId,
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(election.ToResponse(clock.UtcNow, myChoices: null));
    }
}
