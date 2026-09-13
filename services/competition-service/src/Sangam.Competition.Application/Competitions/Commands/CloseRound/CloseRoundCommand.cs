using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;

namespace Sangam.Competition.Application.Competitions.Commands.CloseRound;

/// <summary>Closes a round. After this, no new entries or scores are accepted, and its result may be published.</summary>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record CloseRoundCommand(Guid CompetitionId, Guid RoundId) : ICommand<RoundResponse>;

public sealed class CloseRoundCommandValidator : AbstractValidator<CloseRoundCommand>
{
    public CloseRoundCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.RoundId).NotEmpty();
    }
}

public sealed class CloseRoundCommandHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IRoundResultRepository roundResults,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CloseRoundCommand, Result<RoundResponse>>
{
    public async Task<Result<RoundResponse>> Handle(
        CloseRoundCommand command, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null
            || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<RoundResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var round = competition.FindRound(command.RoundId);

        if (round is null)
        {
            return Result.Failure<RoundResponse>(
                Error.NotFound("Round.NotFound", "No such round in this competition."));
        }

        if (!competition.CloseRound(command.RoundId, clock.UtcNow))
        {
            return Result.Failure<RoundResponse>(Error.Conflict(
                "Round.AlreadyClosed", "This round is already closed."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var entryCount = await entries.CountByRoundAsync(round.Id, cancellationToken);
        var hasResult = await roundResults.FindByRoundAsync(round.Id, cancellationToken) is not null;

        return Result.Success(round.ToResponse(entryCount, hasResult));
    }
}
