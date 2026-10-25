using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Security;
using Sangam.Panch.Domain.Elections;
using Sangam.Panch.Domain.Panel;

namespace Sangam.Panch.Application.Elections.Commands.PublishElectionResults;

/// <summary>
/// Computes the ranking, freezes it, and seats the winners as the Samaaj Panch.
/// </summary>
/// <remarks>
/// Its own command rather than another status move, because it is the only one
/// that produces something: an <see cref="ElectionResult"/> holding the order,
/// stored once - a result recomputed on every read could change after it was
/// announced. Only from <see cref="ElectionStatus.Closed"/>.
///
/// <b>Publishing replaces the whole panel.</b> The ten winners (fewer, if fewer
/// candidates got a vote) become the roster and everyone previously seated is
/// unseated, in the same transaction, and one <c>panch.panel.changed.v1</c>
/// carrying the new roster is announced. Winners need at least one vote: a seat
/// nobody voted for is not an election result.
/// </remarks>
[RequiresPermission(PermissionKeys.PanchManage)]
public sealed record PublishElectionResultsCommand(Guid ElectionId) : ICommand<ElectionResultResponse>;

public sealed class PublishElectionResultsCommandValidator : AbstractValidator<PublishElectionResultsCommand>
{
    public PublishElectionResultsCommandValidator() => RuleFor(x => x.ElectionId).NotEmpty();
}

public sealed class PublishElectionResultsCommandHandler(
    IElectionRepository elections,
    IBallotRepository ballots,
    IPanelRepository panel,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<PublishElectionResultsCommandHandler> logger)
    : IRequestHandler<PublishElectionResultsCommand, Result<ElectionResultResponse>>
{
    public async Task<Result<ElectionResultResponse>> Handle(
        PublishElectionResultsCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<ElectionResultResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var election = await elections.GetByIdAsync(command.ElectionId, cancellationToken);

        if (election is null
            || (tenantContext.TenantId is not { } tenantId || election.TenantId != tenantId))
        {
            return Result.Failure<ElectionResultResponse>(
                Error.NotFound("Election.NotFound", "No such election in this Samaaj."));
        }

        if (election.Status == ElectionStatus.Published)
        {
            // Already announced. Returning the stored result rather than
            // recomputing is the whole point of storing it.
            var already = await ballots.FindResultAsync(election.Id, cancellationToken);

            if (already is not null)
            {
                return Result.Success(await DescribeAsync(election, already, cancellationToken));
            }
        }

        var now = clock.UtcNow;

        if (!election.MoveTo(ElectionStatus.Published, actorId, now))
        {
            return Result.Failure<ElectionResultResponse>(Error.Conflict(
                "Election.NotClosed",
                "Close the election before publishing its result. Publishing while votes are "
                + "still arriving would announce an answer to a question still being asked."));
        }

        var tally = await ballots.TallyAsync(election.Id, cancellationToken);

        var ranked = election.RankBy(tally).Select(c => c.Id).ToList();
        var winners = election.Winners(tally);
        var seated = winners.Select(c => c.MemberId).ToList();

        ballots.AddResult(new ElectionResult(election.Id, ranked, seated, actorId, now));

        // Replace the panel: everyone seated before is unseated, the winners sit.
        foreach (var previous in await panel.ListAsync(cancellationToken))
        {
            panel.Remove(previous);
        }

        foreach (var memberId in seated)
        {
            panel.Add(new PanelMember(tenantId, memberId, election.Id, now));
        }

        election.AnnouncePanel(seated, actorId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Election {ElectionId} published; {Count} member(s) seated", election.Id, seated.Count);

        var stored = await ballots.FindResultAsync(election.Id, cancellationToken);

        return Result.Success(await DescribeAsync(election, stored!, cancellationToken));
    }

    private async Task<ElectionResultResponse> DescribeAsync(
        PanchElection election, ElectionResult result, CancellationToken cancellationToken)
    {
        var tally = await ballots.TallyAsync(election.Id, cancellationToken);

        return new ElectionResultResponse(
            election.Id,
            [
                .. result.RankedCandidateIds.Select((candidateId, index) => new ResultEntry(
                    index + 1,
                    candidateId,
                    election.FindCandidate(candidateId)?.MemberId ?? Guid.Empty,
                    tally.GetValueOrDefault(candidateId)))
            ],
            result.SeatedMemberIds,
            result.PublishedBy,
            result.PublishedAt);
    }
}
