using FluentValidation;
using MediatR;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;
using Sangam.Events.Domain.Events;

namespace Sangam.Events.Application.Events.Commands.UpdateEvent;

/// <summary>
/// Corrects an event's own details - title, description, dates, venue, type,
/// capacity, and whether registration is open.
/// </summary>
/// <remarks>
/// Allowed on a Draft or a Published event, refused on a Cancelled one -
/// there is nothing left to correct once an event is called off, and
/// `Cancel` already keeps the reason people were told. Publishing and
/// cancelling stay their own commands, unchanged: this one only ever
/// touches the fields above, never the status.
///
/// The organiser (<c>OrganizerType</c>/<c>OrganizerId</c>) is deliberately
/// not editable here. Reassigning who is holding an event that may already
/// have registrations against it is a bigger structural change than a
/// correction, and nothing asked for it - this command stays the same
/// narrow shape <c>CreateEventCommand</c> already established for its own
/// fields.
/// </remarks>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record UpdateEventCommand(
    Guid EventId,
    string Title,
    string? Description,
    DateTimeOffset StartAt,
    DateTimeOffset? EndAt,
    string? Venue,
    string? EventTypeId,
    bool RegistrationEnabled,
    int? Capacity,

    // The locality the event is for; null for none. An Area Admin cannot move
    // an event out of their own locality - omitted, it stays there.
    string? LocalityId = null) : ICommand<EventResponse>;

public sealed class UpdateEventCommandValidator : AbstractValidator<UpdateEventCommand>
{
    public UpdateEventCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(5000);
        RuleFor(x => x.Venue).MaximumLength(300);
        RuleFor(x => x.EventTypeId).Must(BeNullOrAGuid).WithMessage("Event type is not valid.");
        RuleFor(x => x.LocalityId).Must(BeNullOrAGuid).WithMessage("Locality is not valid.");

        RuleFor(x => x.EndAt)
            .GreaterThan(x => x.StartAt)
            .WithMessage("An event cannot end before it starts.")
            .When(x => x.EndAt.HasValue);

        RuleFor(x => x.Capacity)
            .GreaterThan(0)
            .WithMessage("Capacity must be at least one. Leave it empty for no limit.")
            .When(x => x.Capacity.HasValue);
    }

    private static bool BeNullOrAGuid(string? value) => value is null || Guid.TryParse(value, out _);
}

public sealed class UpdateEventCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateEventCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        UpdateEventCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var samaajEvent = await events.GetByIdAsync(command.EventId, cancellationToken);

        if (samaajEvent is null
            || (tenantContext.TenantId is { } tenantId && samaajEvent.TenantId != tenantId)
            || !areaScope.MayAdminister(samaajEvent.LocalityId))
        {
            return Result.Failure<EventResponse>(
                Error.NotFound("Event.NotFound", "No such event in this Samaaj."));
        }

        var localityId = command.LocalityId is null ? areaScope.AreaLocalityId : Guid.Parse(command.LocalityId);

        if (!areaScope.MayAdminister(localityId))
        {
            return Result.Failure<EventResponse>(Error.Forbidden(
                "Event.OutsideLocality", "You cannot move an event out of your own locality."));
        }

        if (samaajEvent.Status == EventStatus.Cancelled)
        {
            return Result.Failure<EventResponse>(Error.Conflict(
                "Event.Cancelled", "A cancelled event cannot be edited."));
        }

        if (command.Capacity is { } newCapacity && newCapacity < samaajEvent.RegisteredCount)
        {
            return Result.Failure<EventResponse>(Error.Conflict(
                "Event.CapacityBelowRegistered",
                $"Capacity cannot be set below the {samaajEvent.RegisteredCount} " +
                "people already registered."));
        }

        samaajEvent.Update(
            command.Title,
            command.Description,
            command.StartAt,
            command.EndAt,
            command.Venue,
            // Guaranteed null-or-parseable by UpdateEventCommandValidator.
            command.EventTypeId is null ? null : Guid.Parse(command.EventTypeId),
            command.RegistrationEnabled,
            command.Capacity,
            clock.UtcNow,
            localityId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(samaajEvent.ToResponse(memberId));
    }
}
