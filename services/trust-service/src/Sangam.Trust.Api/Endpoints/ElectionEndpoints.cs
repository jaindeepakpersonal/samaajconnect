using MediatR;
using Sangam.Trust.Api.Extensions;
using Sangam.Trust.Application.Elections;
using Sangam.Trust.Application.Elections.Commands.CastTrustVote;
using Sangam.Trust.Application.Elections.Commands.DecideTrustCandidate;
using Sangam.Trust.Application.Elections.Commands.MoveTrustElection;
using Sangam.Trust.Application.Elections.Commands.NominateTrustCandidate;
using Sangam.Trust.Application.Elections.Commands.OpenTrustElection;
using Sangam.Trust.Application.Elections.Commands.PublishTrustElectionResults;
using Sangam.Trust.Application.Elections.Queries;

namespace Sangam.Trust.Api.Endpoints;

/// <summary>
/// Thin mapping only (root CLAUDE.md section 4.6): bind, build the request,
/// send, map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class ElectionEndpoints
{
    public static IEndpointRouteBuilder MapElectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/trust").WithTags("Trust elections");

        group.MapGet("/trusts/{trustId:guid}/elections", async (
                Guid trustId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListTrustElectionsQuery(trustId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListTrustElections")
            .WithSummary("This Trust's elections, newest first, with the asking member's own vote on each.")
            .Produces<IReadOnlyList<TrustElectionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/trusts/{trustId:guid}/elections", async (
                Guid trustId,
                OpenElectionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new OpenTrustElectionCommand(
                    trustId,
                    request.RoleId,
                    request.NominationStartAt,
                    request.NominationEndAt,
                    request.VotingStartAt,
                    request.VotingEndAt);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/trust/elections/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("OpenTrustElection")
            .WithSummary("Set an election up for one of this Trust's roles. It stays a draft until opened.")
            .Produces<TrustElectionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/elections/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetTrustElectionQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetTrustElection")
            .WithSummary("One election with its ballot and tally.")
            .Produces<TrustElectionDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/elections/{id:guid}/status", async (
                Guid id,
                MoveRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new MoveTrustElectionCommand(id, request.Status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("MoveTrustElection")
            .WithSummary("Open nominations, open voting, or close. Strictly forward.")
            .Produces<TrustElectionResponse>()
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
                    new NominateTrustCandidateCommand(id, request.MemberId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("NominateTrustCandidate")
            .WithSummary("Put a Trust member forward. An admin decides whether they reach the ballot.")
            .Produces<NominateTrustCandidateResponse>()
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
                    new DecideTrustCandidateCommand(id, candidateId, request.Approve),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DecideTrustCandidate")
            .WithSummary("Put a nomination on the ballot, or remove it before voting opens.")
            .Produces<TrustElectionDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/votes", async (
                Guid id,
                VoteRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CastTrustVoteCommand(id, request.CandidateIds), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CastTrustVote")
            .WithSummary(
                "Cast this member's one vote. Voting twice is reported as success with "
                + "accepted=false; the unique index, not this endpoint, is what enforces it.")
            .Produces<CastTrustVoteResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/publish", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PublishTrustElectionResultsCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PublishTrustElectionResults")
            .WithSummary("Pick the winner, freeze it, and install them in the position. Only from Closed.")
            .Produces<TrustElectionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record OpenElectionRequest(
        Guid RoleId,
        DateTimeOffset NominationStartAt,
        DateTimeOffset NominationEndAt,
        DateTimeOffset VotingStartAt,
        DateTimeOffset VotingEndAt);

    /// <summary>NominationsOpen, VotingOpen or Closed. Publishing is its own call.</summary>
    public sealed record MoveRequest(string Status);

    public sealed record NominateRequest(string MemberId);

    /// <summary>
    /// <paramref name="Approve"/> is required and has no default: a decision
    /// endpoint whose safest value is implicit is one where a mistyped
    /// request quietly puts somebody on a ballot.
    /// </summary>
    public sealed record DecideRequest(bool Approve);

    /// <summary>Up to the election's seats, in order of preference.</summary>
    public sealed record VoteRequest(IReadOnlyList<Guid> CandidateIds);
}
