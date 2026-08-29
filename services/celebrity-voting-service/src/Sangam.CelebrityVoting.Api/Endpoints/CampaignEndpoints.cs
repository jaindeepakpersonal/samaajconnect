using MediatR;
using Sangam.CelebrityVoting.Api.Extensions;
using Sangam.CelebrityVoting.Application.Campaigns;
using Sangam.CelebrityVoting.Application.Campaigns.Commands.CastVote;
using Sangam.CelebrityVoting.Application.Campaigns.Commands.CreateCampaign;
using Sangam.CelebrityVoting.Application.Campaigns.Commands.DecideCandidate;
using Sangam.CelebrityVoting.Application.Campaigns.Commands.MoveCampaign;
using Sangam.CelebrityVoting.Application.Campaigns.Commands.NominateCandidate;
using Sangam.CelebrityVoting.Application.Campaigns.Commands.PublishResults;
using Sangam.CelebrityVoting.Application.Campaigns.Queries;

namespace Sangam.CelebrityVoting.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class CampaignEndpoints
{
    public static IEndpointRouteBuilder MapCampaignEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/celebrity-voting").WithTags("Celebrity voting");

        group.MapGet("/campaigns", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListCampaignsQuery(), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListCampaigns")
            .WithSummary("This Samaaj's campaigns, with the asking member's own vote on each.")
            .Produces<IReadOnlyList<CampaignResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/campaigns", async (
                CreateCampaignRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateCampaignCommand(
                    request.Title,
                    request.Description,
                    request.NominationStartAt,
                    request.NominationEndAt,
                    request.VotingStartAt,
                    request.VotingEndAt,
                    request.TopN,
                    request.ResultsVisibility);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/celebrity-voting/campaigns/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateCampaign")
            .WithSummary("Set a campaign up. It stays a draft until nominations are opened.")
            .Produces<CampaignResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/campaigns/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCampaignQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetCampaign")
            .WithSummary("One campaign with its ballot, and the tally if this caller may see it.")
            .Produces<CampaignDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/campaigns/{id:guid}/status", async (
                Guid id,
                MoveRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new MoveCampaignCommand(id, request.Status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("MoveCampaign")
            .WithSummary("Open nominations, open voting, or close. Strictly forward.")
            .Produces<CampaignResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/campaigns/{id:guid}/candidates", async (
                Guid id,
                NominateRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new NominateCandidateCommand(id, request.MemberId, request.Category),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("NominateCandidate")
            .WithSummary("Put a member forward. A reviewer decides whether they reach the ballot.")
            .Produces<NominateResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/campaigns/{id:guid}/candidates/{candidateId:guid}/decide", async (
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
            .Produces<CampaignDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/campaigns/{id:guid}/votes", async (
                Guid id,
                VoteRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CastVoteCommand(id, request.CandidateId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CastVote")
            .WithSummary(
                "Cast this member's one vote. Voting twice is reported as success with "
                + "accepted=false; the unique index, not this endpoint, is what enforces it.")
            .Produces<CastVoteResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/campaigns/{id:guid}/results", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PublishResultsCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PublishResults")
            .WithSummary("Compute the ranking and freeze it. Only from Closed.")
            .Produces<CampaignResultResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/campaigns/{id:guid}/results", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetResultsQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetResults")
            .WithSummary("The published ranking, as frozen when it was announced.")
            .Produces<CampaignResultResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record CreateCampaignRequest(
        string Title,
        string? Description,
        DateTimeOffset NominationStartAt,
        DateTimeOffset NominationEndAt,
        DateTimeOffset VotingStartAt,
        DateTimeOffset VotingEndAt,
        int TopN,
        string ResultsVisibility);

    /// <summary>NominationsOpen, VotingOpen or Closed. Publishing is its own call.</summary>
    public sealed record MoveRequest(string Status);

    public sealed record NominateRequest(Guid MemberId, string? Category);

    /// <summary>
    /// <paramref name="Approve"/> is required and has no default: a decision
    /// endpoint whose safest value is implicit is one where a mistyped request
    /// quietly puts somebody on a ballot.
    /// </summary>
    public sealed record DecideRequest(bool Approve);

    public sealed record VoteRequest(Guid CandidateId);
}
