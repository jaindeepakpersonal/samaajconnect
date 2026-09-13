using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;

namespace Sangam.Competition.Application.Competitions.Commands.CreateRound;

/// <summary>
/// Opens a new round, created Open. There is no automatic advancement
/// between rounds - the organiser creates entries for whoever they want
/// competing in it, by hand, after creating it. See `Round`'s own remarks.
/// </summary>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record CreateRoundCommand(Guid CompetitionId, string Name) : ICommand<RoundResponse>;

public sealed class CreateRoundCommandValidator : AbstractValidator<CreateRoundCommand>
{
    public CreateRoundCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public sealed class CreateRoundCommandHandler(
    ICompetitionRepository competitions,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CreateRoundCommand, Result<RoundResponse>>
{
    public async Task<Result<RoundResponse>> Handle(
        CreateRoundCommand command, CancellationToken cancellationToken)
    {
        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null
            || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<RoundResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var round = competition.CreateRound(command.Name, clock.UtcNow);

        if (round is null)
        {
            return Result.Failure<RoundResponse>(Error.Conflict(
                "Competition.Closed", "This competition is closed; no new rounds can be added."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(round.ToResponse(entryCount: 0, hasPublishedResult: false));
    }
}
