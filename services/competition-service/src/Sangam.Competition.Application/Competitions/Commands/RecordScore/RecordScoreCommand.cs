using FluentValidation;
using MediatR;
using Sangam.Competition.Application.Abstractions;
using Sangam.Competition.Application.Common;
using Sangam.Competition.Application.Security;
using Sangam.Competition.Domain.Competitions;

namespace Sangam.Competition.Application.Competitions.Commands.RecordScore;

/// <summary>
/// Records or corrects one judge's mark for one entry, while its round is
/// open.
/// </summary>
/// <remarks>
/// An upsert, unlike celebrity-voting-service's own `CastVoteCommand` -
/// correcting a misrecorded mark before judging closes is the ordinary case
/// here, not ballot-stuffing. See `Score`'s own remarks and
/// `IScoreRepository.UpsertAsync`.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record RecordScoreCommand(Guid CompetitionId, Guid EntryId, int Value, string? Comments)
    : ICommand<ScoreResponse>;

public sealed class RecordScoreCommandValidator : AbstractValidator<RecordScoreCommand>
{
    public RecordScoreCommandValidator()
    {
        RuleFor(x => x.CompetitionId).NotEmpty();
        RuleFor(x => x.EntryId).NotEmpty();
        RuleFor(x => x.Value).InclusiveBetween(0, 100);
        RuleFor(x => x.Comments).MaximumLength(500);
    }
}

public sealed class RecordScoreCommandHandler(
    ICompetitionRepository competitions,
    IEntryRepository entries,
    IScoreRepository scores,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RecordScoreCommand, Result<ScoreResponse>>
{
    public async Task<Result<ScoreResponse>> Handle(
        RecordScoreCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } judgeId)
        {
            return Result.Failure<ScoreResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var competition = await competitions.GetByIdAsync(command.CompetitionId, cancellationToken);

        if (competition is null
            || (tenantContext.TenantId is { } tenantId && competition.TenantId != tenantId))
        {
            return Result.Failure<ScoreResponse>(
                Error.NotFound("Competition.NotFound", "No such competition in this Samaaj."));
        }

        if (!competition.IsJudge(judgeId))
        {
            return Result.Failure<ScoreResponse>(Error.Forbidden(
                "Judge.Required", "You are not an assigned judge for this competition."));
        }

        var entry = await entries.GetByIdAsync(command.EntryId, cancellationToken);

        if (entry is null || entry.TenantId != competition.TenantId)
        {
            return Result.Failure<ScoreResponse>(
                Error.NotFound("Entry.NotFound", "No such entry in this competition."));
        }

        var round = competition.FindRound(entry.RoundId);

        if (round is null)
        {
            return Result.Failure<ScoreResponse>(
                Error.NotFound("Entry.NotFound", "No such entry in this competition."));
        }

        if (round.Status != RoundStatus.Open)
        {
            return Result.Failure<ScoreResponse>(Error.Conflict(
                "Round.Closed", "This round is closed; scores can no longer be recorded."));
        }

        var now = clock.UtcNow;

        var score = await scores.UpsertAsync(
            competition.TenantId, entry.Id, judgeId, command.Value, command.Comments, now, cancellationToken);

        return Result.Success(new ScoreResponse(
            score.EntryId, score.JudgeMemberId, score.Value, score.Comments, score.UpdatedAt));
    }
}
