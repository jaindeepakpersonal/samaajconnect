using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;
using Sangam.Competition.Domain.Entries;

namespace Sangam.Competition.Application.Competitions.Commands.PublishRoundResults;

/// <summary>
/// Computes a round's ranking and freezes it.
/// </summary>
/// <remarks>
/// Only from a closed round - publishing while scores are still arriving
/// would announce an answer to a question still being asked, the same
/// restraint celebrity-voting-service's own `PublishResultsCommand` takes.
/// Publishing twice returns the frozen result rather than recomputing it.
/// </remarks>
[RequiresPermission(PermissionKeys.CompetitionManage)]
public sealed record PublishRoundResultsCommand(Guid CompetitionId, Guid RoundId)
    : ICommand<RoundResultResponse>;

public sealed class PublishRoundResultsCommandValidator
    : AbstractValidator<PublishRoundResultsCommand>
{
    public PublishRoundResultsCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.RoundId).NotEmpty();
    }
}

public sealed class PublishRoundResultsCommandHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IScoreRepository scores,
    IRoundResultRepository roundResults,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<PublishRoundResultsCommandHandler> logger)
    : IRequestHandler<PublishRoundResultsCommand, Result<RoundResultResponse>>
{
    public async Task<Result<RoundResultResponse>> Handle(
        PublishRoundResultsCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<RoundResultResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null
            || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<RoundResultResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        var round = competition.FindRound(command.RoundId);

        if (round is null)
        {
            return Result.Failure<RoundResultResponse>(
                Error.NotFound("Round.NotFound", "No such round in this competition."));
        }

        var already = await roundResults.FindByRoundAsync(round.Id, cancellationToken);

        if (already is not null)
        {
            return Result.Success(await DescribeAsync(already));
        }

        if (round.Status != RoundStatus.Closed)
        {
            return Result.Failure<RoundResultResponse>(Error.Conflict(
                "Round.NotClosed",
                "Close the round before publishing its result. Publishing while scores are "
                + "still arriving would announce an answer to a question still being asked."));
        }

        var roundEntries = await entries.ListByRoundAsync(round.Id, cancellationToken);

        if (roundEntries.Count == 0)
        {
            return Result.Failure<RoundResultResponse>(Error.Conflict(
                "Round.NoEntries", "This round has no entries to rank."));
        }

        var entryIds = roundEntries.Select(e => e.Id).ToList();
        var averages = await scores.AverageByEntryAsync(entryIds, cancellationToken);

        var ranked = roundEntries.RankBy(averages).Select(e => e.Id).ToList();

        var now = clock.UtcNow;
        var result = new RoundResult(competition.TenantId, round.Id, ranked, actorId, now);

        roundResults.Add(result);
        competition.AnnounceRoundResults(round.Id, ranked, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Round {RoundId} of competition {CompetitionId} published with {Count} entries",
            round.Id, competition.Id, ranked.Count);

        return Result.Success(await DescribeAsync(result));

        // Self-contained: re-reads the round's own entries rather than
        // closing over `roundEntries` above, which is unassigned on the
        // "already published" early-return path this is also called from.
        async Task<RoundResultResponse> DescribeAsync(RoundResult frozen)
        {
            var freshEntries = await entries.ListByRoundAsync(frozen.RoundId, cancellationToken);
            var byId = freshEntries.ToDictionary(e => e.Id);
            var freshAverages = await scores.AverageByEntryAsync(frozen.RankedEntryIds, cancellationToken);

            var ranking = frozen.RankedEntryIds
                .Select((entryId, index) => new RoundResultEntry(
                    index + 1,
                    entryId,
                    byId.TryGetValue(entryId, out var e) ? e.ParticipantMemberId : Guid.Empty,
                    freshAverages.GetValueOrDefault(entryId)))
                .ToList();

            return new RoundResultResponse(frozen.RoundId, ranking, frozen.PublishedBy, frozen.PublishedAt);
        }
    }
}
