using MediatR;
using Sangam.VolunteerGroups.Api.Extensions;
using Sangam.VolunteerGroups.Application.Elections;
using Sangam.VolunteerGroups.Application.Elections.Commands.CastVote;
using Sangam.VolunteerGroups.Application.Elections.Commands.DecideCandidate;
using Sangam.VolunteerGroups.Application.Elections.Commands.MoveElection;
using Sangam.VolunteerGroups.Application.Elections.Commands.NominateCandidate;
using Sangam.VolunteerGroups.Application.Elections.Commands.OpenElection;
using Sangam.VolunteerGroups.Application.Elections.Commands.PublishElectionResults;
using Sangam.VolunteerGroups.Application.Elections.Commands.UpdateElection;
using Sangam.VolunteerGroups.Application.Elections.Queries;

namespace Sangam.VolunteerGroups.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class ElectionEndpoints
{
    public static IEndpointRouteBuilder MapElectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/volunteer-groups").WithTags("Volunteer group elections");

        group.MapGet("/groups/{groupId:guid}/elections", async (
                Guid groupId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListElectionsQuery(groupId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListElections")
            .WithSummary("This group's elections, newest first, with the asking member's own vote on each.")
            .Produces<IReadOnlyList<ElectionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/groups/{groupId:guid}/elections", async (
                Guid groupId,
                OpenElectionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new OpenElectionCommand(
                    groupId,
                    request.RoleId,
                    request.NominationStartAt,
                    request.NominationEndAt,
                    request.VotingStartAt,
                    request.VotingEndAt);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/volunteer-groups/elections/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("OpenElection")
            .WithSummary("The president or an administrator sets an election up for one of the group's roles. It stays a draft until opened.")
            .Produces<ElectionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

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
            .WithSummary("One election with its ballot and tally.")
            .Produces<ElectionDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/elections/{id:guid}", async (
                Guid id,
                OpenElectionRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new UpdateElectionCommand(
                    id,
                    request.RoleId,
                    request.NominationStartAt,
                    request.NominationEndAt,
                    request.VotingStartAt,
                    request.VotingEndAt);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateElection")
            .WithSummary(
                "Correct the position title or windows - how nominations that closed too early "
                + "are reopened. Allowed in Draft or NominationsOpen; refused from VotingOpen on.")
            .Produces<ElectionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
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
            .WithSummary("Open nominations, open voting, or close. Strictly forward.")
            .Produces<ElectionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
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
            .WithSummary("Put a group member forward. The president decides whether they reach the ballot.")
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
                    new DecideCandidateCommand(id, candidateId, request.Approve),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("DecideCandidate")
            .WithSummary("Put a nomination on the ballot, or remove it before voting opens.")
            .Produces<ElectionDetailResponse>()
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
                    new CastVoteCommand(id, request.CandidateIds), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CastVote")
            .WithSummary(
                "Cast this member's one ballot: up to as many candidates as the role has seats. "
                + "Voting twice is reported as success with accepted=false; the unique indexes, "
                + "not this endpoint, are what enforce it.")
            .Produces<CastVoteResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/elections/{id:guid}/publish", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PublishElectionResultsCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PublishElectionResults")
            .WithSummary("Pick as many winners as the role has seats, freeze them, and install them in the role. Only from Closed.")
            .Produces<ElectionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record OpenElectionRequest(
        string RoleId,
        DateTimeOffset NominationStartAt,
        DateTimeOffset NominationEndAt,
        DateTimeOffset VotingStartAt,
        DateTimeOffset VotingEndAt);

    /// <summary>NominationsOpen, VotingOpen or Closed. Publishing is its own call.</summary>
    public sealed record MoveRequest(string Status);

    public sealed record NominateRequest(string MemberId);

    /// <summary>
    /// <paramref name="Approve"/> is required and has no default: a decision
    /// endpoint whose safest value is implicit is one where a mistyped request
    /// quietly puts somebody on a ballot.
    /// </summary>
    public sealed record DecideRequest(bool Approve);

    /// <summary>Up to as many candidates as the role has seats - one final ballot.</summary>
    public sealed record VoteRequest(IReadOnlyList<Guid> CandidateIds);
}
