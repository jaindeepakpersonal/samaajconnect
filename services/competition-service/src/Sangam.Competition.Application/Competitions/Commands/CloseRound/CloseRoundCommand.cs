using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;

namespace Sangam.Competition.Application.Competitions.Commands.CloseRound;

/// <summary>Closes a round. After this, no new entries or scores are accepted, and its result may be published.</summary>
/// <remarks>
/// Closing the audition also decides it: the top `FinalistCount` by average
/// audition mark - and everyone tied with the last of them - are entered in
/// every main round, in the same transaction. Somebody no judge marked did not
/// audition and does not go through, so an audition nobody has marked is
/// refused rather than closed with no finalists.
/// </remarks>
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
    IScoreRepository scores,
    IRoundResultRepository roundResults,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
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

        var now = clock.UtcNow;
        IReadOnlyList<Guid> finalists = [];

        if (round.Kind == RoundKind.Audition && round.Status == RoundStatus.Open)
        {
            var auditioned = await entries.ListByRoundAsync(round.Id, cancellationToken);
            var averages = await scores.AverageByEntryAsync([.. auditioned.Select(e => e.Id)], cancellationToken);

            finalists = CompetitionScoring.SelectFinalists(
                [.. auditioned.Select(e => (e.ParticipantMemberId, averages.TryGetValue(e.Id, out var a) ? a : (double?)null))],
                competition.FinalistCount ?? auditioned.Count);

            if (finalists.Count == 0)
            {
                return Result.Failure<RoundResponse>(Error.Conflict(
                    "Audition.NobodyMarked",
                    "No judge has marked anybody in the audition yet, so nobody would go through."));
            }
        }

        if (!competition.CloseRound(command.RoundId, now))
        {
            return Result.Failure<RoundResponse>(Error.Conflict(
                "Round.AlreadyClosed", "This round is already closed."));
        }

        if (finalists.Count > 0)
        {
            await CompetitionJudging.EnterAsync(
                entries, competition.TenantId, competition.MainRounds, finalists, now, cancellationToken);

            competition.AnnounceFinalists(finalists, currentUser.UserId ?? Guid.Empty, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var entryCount = await entries.CountByRoundAsync(round.Id, cancellationToken);
        var hasResult = await roundResults.FindByRoundAsync(round.Id, cancellationToken) is not null;

        return Result.Success(round.ToResponse(entryCount, hasResult));
    }
}
