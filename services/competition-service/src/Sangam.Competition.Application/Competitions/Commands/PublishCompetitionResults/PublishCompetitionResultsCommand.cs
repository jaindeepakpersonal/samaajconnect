using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Competitions.Queries;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;
using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Application.Competitions.Commands.PublishCompetitionResults;

/// <summary>
/// Publishes the final standings - every participant's total of their main
/// round averages, best first - and freezes them.
/// </summary>
/// <remarks>
/// Only once every main round is closed, the same restraint publishing a
/// round already takes: standings published while a judge is still marking
/// would announce an answer that then moves. Publishing twice returns the
/// frozen result. Audition marks never count; they decided who took part.
/// </remarks>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record PublishCompetitionResultsCommand(Guid CompetitionId) : ICommand<CompetitionResultResponse>;

public sealed class PublishCompetitionResultsCommandValidator : AbstractValidator<PublishCompetitionResultsCommand>
{
    public PublishCompetitionResultsCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
    }
}

public sealed class PublishCompetitionResultsCommandHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IScoreRepository scores,
    ICompetitionResultRepository results,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<PublishCompetitionResultsCommandHandler> logger)
    : IRequestHandler<PublishCompetitionResultsCommand, Result<CompetitionResultResponse>>
{
    public async Task<Result<CompetitionResultResponse>> Handle(
        PublishCompetitionResultsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<CompetitionResultResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null
            || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<CompetitionResultResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var already = await results.FindByCompetitionAsync(competition.Id, cancellationToken);

        if (already is not null)
        {
            return Result.Success(
                await CompetitionResultDescriber.DescribeAsync(competition, already, true, entries, scores, cancellationToken));
        }

        var mainRounds = competition.MainRounds;

        if (mainRounds.Count == 0)
        {
            return Result.Failure<CompetitionResultResponse>(Error.Conflict(
                "Competition.NotJudged", "Judging has not started, so there is nothing to publish."));
        }

        if (mainRounds.Any(r => r.Status == RoundStatus.Open))
        {
            return Result.Failure<CompetitionResultResponse>(Error.Conflict(
                "Competition.RoundsOpen",
                "Close every round before publishing the result. A result published while judges "
                + "are still marking would move after it was announced."));
        }

        var mainEntries = await entries.ListByRoundsAsync([.. mainRounds.Select(r => r.Id)], cancellationToken);

        if (mainEntries.Count == 0)
        {
            return Result.Failure<CompetitionResultResponse>(Error.Conflict(
                "Competition.NoEntries", "Nobody took part in the rounds, so there is nobody to rank."));
        }

        var averages = await scores.AverageByEntryAsync([.. mainEntries.Select(e => e.Id)], cancellationToken);
        var standings = CompetitionJudging.Standings(competition, mainEntries, averages);
        var ranked = standings.Select(s => s.MemberId).ToList();

        var now = clock.UtcNow;
        var result = new CompetitionResult(competition.TenantId, competition.Id, ranked, actorId, now);

        results.Add(result);
        competition.AnnounceResults(ranked, actorId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Competition {CompetitionId} published its result with {Count} participants", competition.Id, ranked.Count);

        return Result.Success(
            await CompetitionResultDescriber.DescribeAsync(competition, result, true, entries, scores, cancellationToken));
    }
}
