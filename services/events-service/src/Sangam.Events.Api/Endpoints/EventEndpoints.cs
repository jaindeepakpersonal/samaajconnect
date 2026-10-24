using MediatR;
using Sangam.Events.Api.Extensions;
using Sangam.Events.Application.Events;
using Sangam.Events.Application.Events.Commands.CancelEvent;
using Sangam.Events.Application.Events.Commands.CancelRegistration;
using Sangam.Events.Application.Events.Commands.CreateEvent;
using Sangam.Events.Application.Events.Commands.EventSchedule;
using Sangam.Events.Application.Events.Commands.PublishEvent;
using Sangam.Events.Application.Events.Commands.RegisterForEvent;
using Sangam.Events.Application.Events.Commands.UpdateEvent;
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
                    request.EventTypeId,
                    request.OrganizerType,
                    request.OrganizerId,
                    request.RegistrationEnabled,
                    request.Capacity,
                    request.LocalityId);

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

        group.MapPatch("/{id:guid}", async (
                Guid id,
                UpdateEventRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var command = new UpdateEventCommand(
                    id,
                    request.Title,
                    request.Description,
                    request.StartAt,
                    request.EndAt,
                    request.Venue,
                    request.EventTypeId,
                    request.RegistrationEnabled,
                    request.Capacity,
                    request.LocalityId);

                var result = await sender.Send(command, cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateEvent")
            .WithSummary("Correct an event's own details. Refused once it is cancelled.")
            .Produces<EventResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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

        // ---- The schedule: an event's programmes --------------------------

        group.MapPost("/{id:guid}/schedule", async (
                Guid id,
                ScheduleItemRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new AddEventScheduleItemCommand(id, request.Date, request.ProgramName, request.StartTime, request.EndTime),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("AddEventScheduleItem")
            .WithSummary("Add a programme to an event's schedule: a date, its name, a start and an optional end time.")
            .Produces<EventResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}/schedule/{itemId:guid}", async (
                Guid id,
                Guid itemId,
                ScheduleItemRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new UpdateEventScheduleItemCommand(
                        id, itemId, request.Date, request.ProgramName, request.StartTime, request.EndTime),
                    cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("UpdateEventScheduleItem")
            .WithSummary("Correct one programme on an event's schedule.")
            .Produces<EventResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{id:guid}/schedule/{itemId:guid}", async (
                Guid id,
                Guid itemId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RemoveEventScheduleItemCommand(id, itemId), cancellationToken);

                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithName("RemoveEventScheduleItem")
            .WithSummary("Take a programme off an event's schedule.")
            .Produces<EventResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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
        string? EventTypeId,
        string OrganizerType,
        Guid? OrganizerId,
        bool RegistrationEnabled,
        int? Capacity,
        string? LocalityId = null);

    /// <summary>The reason is required: members who were going are told it.</summary>
    public sealed record CancelEventRequest(string? Reason);

    /// <summary>Same shape as <see cref="CreateEventRequest"/>, minus the organiser - see `UpdateEventCommand`'s own remarks for why.</summary>
    public sealed record UpdateEventRequest(
        string Title,
        string? Description,
        DateTimeOffset StartAt,
        DateTimeOffset? EndAt,
        string? Venue,
        string? EventTypeId,
        bool RegistrationEnabled,
        int? Capacity,
        string? LocalityId = null);

    /// <summary>
    /// One programme. <c>Date</c> is "yyyy-MM-dd", the times "HH:mm:ss" on the
    /// clock where it happens; <c>EndTime</c> may be null.
    /// </summary>
    public sealed record ScheduleItemRequest(
        DateOnly Date,
        string ProgramName,
        TimeOnly StartTime,
        TimeOnly? EndTime);
}
