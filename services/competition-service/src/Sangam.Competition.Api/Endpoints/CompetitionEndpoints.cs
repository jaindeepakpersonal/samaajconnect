using MediatR;
using Sangam.Competition.Api.Extensions;
using Sangam.Competition.Application.Competitions;
using Sangam.Competition.Application.Competitions.Commands.AssignJudge;
using Sangam.Competition.Application.Competitions.Commands.CloseRound;
using Sangam.Competition.Application.Competitions.Commands.CreateCompetition;
using Sangam.Competition.Application.Competitions.Commands.UpdateCompetition;
using Sangam.Competition.Application.Competitions.Commands.CreateEntry;
using Sangam.Competition.Application.Competitions.Commands.CreateRound;
using Sangam.Competition.Application.Competitions.Commands.MoveCompetition;
using Sangam.Competition.Application.Competitions.Commands.PublishRoundResults;
using Sangam.Competition.Application.Competitions.Commands.RecordScore;
using Sangam.Competition.Application.Competitions.Commands.RegisterForCompetition;
using Sangam.Competition.Application.Competitions.Commands.WithdrawRegistration;
using Sangam.Competition.Application.Competitions.Queries;

namespace Sangam.Competition.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class CompetitionEndpoints
{
    public static IEndpointRouteBuilder MapCompetitionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/competitions").WithTags("Competitions");

        group.MapGet("/", async (Guid? localityId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ListCompetitionsQuery(localityId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListCompetitions")
            .WithSummary("This Samaaj's competitions, each with the asking member's own registration status.")
            .Produces<IReadOnlyList<CompetitionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/", async (
                CreateCompetitionRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new CreateCompetitionCommand(
                    request.Title,
                    request.Description,
                    request.CompetitionTypeId,
                    request.RegistrationStartAt,
                    request.RegistrationEndAt,
                    request.LocalityId);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/competitions/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateCompetition")
            .WithSummary("Set a competition up. It stays a draft until registration is opened.")
            .Produces<CompetitionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPatch("/{id:guid}", async (
                Guid id, CreateCompetitionRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new UpdateCompetitionCommand(
                    id,
                    request.Title,
                    request.Description,
                    request.CompetitionTypeId,
                    request.LocalityId,
                    request.RegistrationStartAt,
                    request.RegistrationEndAt);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateCompetition")
            .WithSummary("Correct a competition's details. Draft or taking registrations only; 409 after.")
            .Produces<CompetitionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCompetitionQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetCompetition")
            .WithSummary("One competition with its rounds and judges.")
            .Produces<CompetitionDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/status", async (
                Guid id, MoveRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new MoveCompetitionCommand(id, request.Status), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("MoveCompetition")
            .WithSummary("Open registration, close it, start judging, or close. Strictly forward.")
            .Produces<CompetitionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/registrations", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RegisterForCompetitionCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RegisterForCompetition")
            .WithSummary("Register the caller. Only while registration is open.")
            .Produces<RegistrationResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/registrations/mine", async (
                Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new WithdrawRegistrationCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("WithdrawRegistration")
            .WithSummary("The caller's own withdrawal.")
            .Produces<RegistrationResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/judges", async (
                Guid id, JudgeRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new AssignJudgeCommand(id, request.MemberId, request.Assign), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AssignJudge")
            .WithSummary("Assign or remove a judge.")
            .Produces<JudgeAssignmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/rounds", async (
                Guid id, RoundRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateRoundCommand(id, request.Name), cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/competitions/{id}/rounds/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateRound")
            .WithSummary("Open a new round. Entries are added to it one at a time; nothing advances automatically.")
            .Produces<RoundResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}/rounds/{roundId:guid}", async (
                Guid id, Guid roundId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetRoundQuery(id, roundId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetRound")
            .WithSummary("A round's entries. Marks are visible only to the organiser or an assigned judge.")
            .Produces<RoundDetailResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/rounds/{roundId:guid}/close", async (
                Guid id, Guid roundId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CloseRoundCommand(id, roundId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CloseRound")
            .WithSummary("Close a round. After this, no new entries or scores are accepted.")
            .Produces<RoundResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/rounds/{roundId:guid}/entries", async (
                Guid id,
                Guid roundId,
                EntryRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CreateEntryCommand(id, roundId, request.ParticipantMemberId), cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/competitions/{id}/rounds/{roundId}/entries/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateEntry")
            .WithSummary("Put one registered participant into this round.")
            .Produces<EntryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/entries/{entryId:guid}/scores", async (
                Guid id,
                Guid entryId,
                ScoreRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new RecordScoreCommand(id, entryId, request.Value, request.Comments), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RecordScore")
            .WithSummary(
                "Record or correct this judge's mark for an entry. An upsert while the round is open.")
            .Produces<ScoreResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/rounds/{roundId:guid}/results", async (
                Guid id, Guid roundId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PublishRoundResultsCommand(id, roundId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PublishRoundResults")
            .WithSummary("Compute the ranking and freeze it. Only from a closed round.")
            .Produces<RoundResultResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}/rounds/{roundId:guid}/results", async (
                Guid id, Guid roundId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetRoundResultQuery(id, roundId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetRoundResult")
            .WithSummary("The published ranking, as frozen when it was announced.")
            .Produces<RoundResultResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record CreateCompetitionRequest(
        string Title,
        string? Description,
        string? CompetitionTypeId,
        DateTimeOffset RegistrationStartAt,
        DateTimeOffset RegistrationEndAt,
        string? LocalityId = null);

    /// <summary>RegistrationOpen, RegistrationClosed, InProgress or Closed.</summary>
    public sealed record MoveRequest(string Status);

    // MemberId/ParticipantMemberId are strings, not Guid: a Guid-typed wire
    // field crashes ASP.NET Core's own JSON model binding with an unhandled
    // 400 the moment the value cannot be parsed. Validated and parsed in the
    // matching command's own validator/handler instead.
    public sealed record JudgeRequest(string MemberId, bool Assign);

    public sealed record RoundRequest(string Name);

    public sealed record EntryRequest(string ParticipantMemberId);

    public sealed record ScoreRequest(int Value, string? Comments);
}
