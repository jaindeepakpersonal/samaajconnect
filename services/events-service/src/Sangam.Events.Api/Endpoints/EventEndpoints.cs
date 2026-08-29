using MediatR;
using Sangam.Events.Api.Extensions;
using Sangam.Events.Application.Events;
using Sangam.Events.Application.Events.Commands.CancelEvent;
using Sangam.Events.Application.Events.Commands.CancelRegistration;
using Sangam.Events.Application.Events.Commands.CreateEvent;
using Sangam.Events.Application.Events.Commands.PublishEvent;
using Sangam.Events.Application.Events.Commands.RegisterForEvent;
using Sangam.Events.Application.Events.Queries.GetAttendees;
using Sangam.Events.Application.Events.Queries.GetEvent;
using Sangam.Events.Application.Events.Queries.ListEvents;

namespace Sangam.Events.Api.Endpoints;

/// <summary>
/// Thin mapping only (CLAUDE.md section 4.6): bind, build the request, send,
/// map the Result. Any `if` past input binding belongs in a handler.
/// </summary>
public static class EventEndpoints
{
    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/events").WithTags("Events");

        group.MapGet("/", async (
                bool? includeDrafts,
                bool? includePast,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var query = new ListEventsQuery(includeDrafts ?? false, includePast ?? false);

                var result = await sender.Send(query, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("ListEvents")
            .WithSummary("The Samaaj's events, each with the asking member's own standing.")
            .Produces<IReadOnlyList<EventResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", async (
                CreateEventRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateEventCommand(
                    request.Title,
                    request.Description,
                    request.StartAt,
                    request.EndAt,
                    request.Venue,
                    request.OrganizerType,
                    request.OrganizerId,
                    request.RegistrationEnabled,
                    request.Capacity);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult(created =>
                    Results.Created($"/v1/events/{created.Id}", created));
            })
            .RequireAuthorization()
            .WithName("CreateEvent")
            .WithSummary("Write an event down. It stays a draft until it is published.")
            .Produces<EventResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetEventQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetEvent")
            .WithSummary("One event.")
            .Produces<EventResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/publish", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PublishEventCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("PublishEvent")
            .WithSummary("Tell the Samaaj about a draft event.")
            .Produces<EventResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/cancel", async (
                Guid id,
                CancelEventRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new CancelEventCommand(id, request.Reason), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CancelEvent")
            .WithSummary("Call an event off. The reason is shown to members who were going.")
            .Produces<EventResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/attendees", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAttendeesQuery(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetAttendees")
            .WithSummary("Who is coming, and who is waiting. The organiser's list.")
            .Produces<IReadOnlyList<AttendeeResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/registration", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RegisterForEventCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RegisterForEvent")
            .WithSummary("RSVP, or join the waitlist when the event is full. Same call for both.")
            .Produces<RegistrationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/registration", async (
                Guid id,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CancelRegistrationCommand(id), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("CancelRegistration")
            .WithSummary("Give up a place, or leave the waitlist. Promotes whoever waited longest.")
            .Produces<CancelRegistrationResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// <paramref name="Capacity"/> null means no limit — a different thing from
    /// a limit of zero, which the validator refuses.
    /// </summary>
    public sealed record CreateEventRequest(
        string Title,
        string? Description,
        DateTimeOffset StartAt,
        DateTimeOffset? EndAt,
        string? Venue,
        string OrganizerType,
        Guid? OrganizerId,
        bool RegistrationEnabled,
        int? Capacity);

    /// <summary>The reason is required: members who were going are told it.</summary>
    public sealed record CancelEventRequest(string? Reason);
}
