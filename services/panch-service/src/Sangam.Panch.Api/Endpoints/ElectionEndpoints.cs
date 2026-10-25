using MediatR;
using Sangam.Panch.Api.Extensions;
using Sangam.Panch.Application.Elections;
using Sangam.Panch.Application.Elections.Commands.CastBallot;
using Sangam.Panch.Application.Elections.Commands.CreateElection;
using Sangam.Panch.Application.Elections.Commands.DecideCandidate;
using Sangam.Panch.Application.Elections.Commands.DeleteElection;
using Sangam.Panch.Application.Elections.Commands.ExtendVoting;
using Sangam.Panch.Application.Elections.Commands.MoveElection;
using Sangam.Panch.Application.Elections.Commands.NominateCandidate;
using Sangam.Panch.Application.Elections.Commands.PublishElectionResults;
using Sangam.Panch.Application.Elections.Commands.UpdateElection;
using Sangam.Panch.Application.Elections.Queries;
using Sangam.Panch.Application.Panel;

namespace Sangam.Panch.Api.Endpoints;

/// <summary>
/// The Samaaj Panch: its seated roster, and the elections that seat it.
/// </summary>
public static class ElectionEndpoints
{
    public static IEndpointRouteBuilder MapElectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/panch").WithTags("Panch");

        group.MapGet("/panel", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPanelQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetPanel")
            .WithSummary("The seated Samaaj Panch, and whether the asker sits on it.")
            .Produces<PanelResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/panel/{memberId:guid}", async (
                Guid memberId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RemovePanelMemberCommand(memberId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RemovePanelMember")
            .WithSummary("Take a member off the Panch. The seat stays empty until the next election.")
            .Produces<PanelResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/elections", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListElectionsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListElections")
            .WithSummary("This Samaaj's Panch elections, with the asking member's own ballot on each.")
            .Produces<IReadOnlyList<ElectionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/elections", async (
                CreateElectionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateElectionCommand(
                    request.Title,
                    request.Description,
                    request.NominationStartAt,
                    request.NominationEndAt,
                    request.VotingStartAt,
                    request.VotingEndAt,
                    request.ResultsVisibility);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/panch/elections/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateElection")
            .WithSummary("Set an election up. It stays a draft until nominations are opened.")
            .Produces<ElectionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/elections/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetElectionQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetElection")
            .WithSummary("One election with its ballot, and the tally if this caller may see it.")
            .Produces<ElectionDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/elections/{id:guid}", async (
                Guid id,
                CreateElectionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new UpdateElectionCommand(
                    id,
                    request.Title,
                    request.Description,
                    request.NominationStartAt,
                    request.NominationEndAt,
                    request.VotingStartAt,
                    request.VotingEndAt,
                    request.ResultsVisibility);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateElection")
            .WithSummary("Correct an election's title, windows or results visibility - before voting opens.")
            .Produces<ElectionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/elections/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new DeleteElectionCommand(id), cancellationToken);

                return result.ToApiResult(_ => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("DeleteElection")
            .WithSummary("Delete an election, with its nominations - only before voting opens.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/extend-voting", async (
                Guid id,
                ExtendVotingRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new ExtendVotingCommand(id, request.VotingEndAt), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ExtendVoting")
            .WithSummary("Keep voting open longer, while it is open. Later only, never earlier.")
            .Produces<ElectionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/status", async (
                Guid id,
                MoveRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new MoveElectionCommand(id, request.Status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("MoveElection")
            .WithSummary("Open nominations, open voting, or close. Never backwards.")
            .Produces<ElectionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/candidates", async (
                Guid id,
                NominateRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new NominateCandidateCommand(id, request.MemberId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("NominateCandidate")
            .WithSummary("Put a member forward. An administrator decides whether they reach the ballot.")
            .Produces<NominateResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/candidates/{candidateId:guid}/decide", async (
                Guid id,
                Guid candidateId,
                DecideRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new DecideCandidateCommand(id, candidateId, request.Approve), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DecideCandidate")
            .WithSummary("Put a nomination on the ballot, or remove it before voting opens.")
            .Produces<ElectionDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/ballots", async (
                Guid id,
                BallotRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CastBallotCommand(id, request.CandidateIds ?? []), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CastBallot")
            .WithSummary("Cast this member's one ballot: up to ten candidates. Final, and idempotent per voter.")
            .Produces<CastBallotResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/results", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PublishElectionResultsCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PublishElectionResults")
            .WithSummary("Freeze the ranking and seat the winners as the Panch, replacing the current one.")
            .Produces<ElectionResultResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/elections/{id:guid}/results", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetResultsQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetElectionResults")
            .WithSummary("The published ranking, as frozen.")
            .Produces<ElectionResultResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record CreateElectionRequest(
        string Title,
        string? Description,
        DateTimeOffset NominationStartAt,
        DateTimeOffset NominationEndAt,
        DateTimeOffset VotingStartAt,
        DateTimeOffset VotingEndAt,
        string ResultsVisibility);

    /// <summary>The new close of voting - later than the current one.</summary>
    public sealed record ExtendVotingRequest(DateTimeOffset VotingEndAt);

    /// <summary>NominationsOpen, VotingOpen or Closed. Publishing is its own call.</summary>
    public sealed record MoveRequest(string Status);

    // MemberId is a string, not Guid: a Guid-typed wire field crashes ASP.NET
    // Core's own JSON model binding with an unhandled 400 the moment the value
    // cannot be parsed. Validated and parsed in the command instead.
    public sealed record NominateRequest(string MemberId);

    /// <summary>
    /// <paramref name="Approve"/> is required and has no default: a decision
    /// endpoint whose safest value is implicit is one where a mistyped request
    /// quietly puts somebody on a ballot.
    /// </summary>
    public sealed record DecideRequest(bool Approve);

    /// <summary>Candidate ids as strings, for the same reason as <see cref="NominateRequest"/>.</summary>
    public sealed record BallotRequest(IReadOnlyList<string>? CandidateIds);
}
