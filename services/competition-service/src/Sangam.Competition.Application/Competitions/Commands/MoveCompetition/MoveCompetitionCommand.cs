using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;

namespace Sangam.Competition.Application.Competitions.Commands.MoveCompetition;

/// <summary>
/// Opens registration, closes it, starts judging, or closes the competition.
/// </summary>
/// <remarks>
/// Strictly forward: Draft to RegistrationOpen to RegistrationClosed to
/// InProgress to Closed. There is no route back and no command to reopen
/// one, the same stance `VotingCampaign`'s own move command takes.
///
/// Starting judging (InProgress) sets the rounds up from the format and enters
/// every registered member: into the audition when there is one - the
/// finalists reach the main rounds when it closes - and otherwise into every
/// main round. Members apply; nobody enters them by hand.
/// </remarks>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record MoveCompetitionCommand(Guid CompetitionId, string Status)
    : ICommand<CompetitionResponse>;

public sealed class MoveCompetitionCommandValidator : AbstractValidator<MoveCompetitionCommand>
{
    public MoveCompetitionCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => Enum.TryParse<CompetitionStatus>(s, ignoreCase: true, out _))
            .WithMessage("Status must be RegistrationOpen, RegistrationClosed, InProgress or Closed.");
    }
}

public sealed class MoveCompetitionCommandHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<MoveCompetitionCommandHandler> logger)
    : IRequestHandler<MoveCompetitionCommand, Result<CompetitionResponse>>
{
    public async Task<Result<CompetitionResponse>> Handle(
        MoveCompetitionCommand command, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null
            || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<CompetitionResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var target = Enum.Parse<CompetitionStatus>(command.Status, ignoreCase: true);

        var now = clock.UtcNow;

        if (!competition.MoveTo(target, now))
        {
            return Result.Failure<CompetitionResponse>(Error.Conflict(
                "Competition.InvalidTransition",
                $"A competition that is {competition.Status} cannot become {target}."));
        }

        if (target == CompetitionStatus.InProgress)
        {
            IReadOnlyList<Round> firstRounds = competition.AuditionRound is { } audition
                ? [audition]
                : competition.MainRounds;

            await CompetitionJudging.EnterAsync(
                entries, competition.TenantId, firstRounds, competition.RegisteredMemberIds, now, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Competition {CompetitionId} moved to {Status}", competition.Id, target);

        return Result.Success(competition.ToResponse(memberId: null));
    }
}
