using FluentValidation;
using MediatR;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;
using Sangam.Events.Domain.Events;

namespace Sangam.Events.Application.Events.Commands.EventSchedule;

// An event's schedule is part of the event, so it is gated exactly like
// UpdateEventCommand: Events.Publish, an Area Admin only for an event in their
// own locality, anyone else told there is no such event, and a cancelled event
// refused. Each command answers with the whole event, so the screen redraws the
// schedule in its printed order without a second request.

/// <summary>The fields one programme carries, shared by add and update.</summary>
public interface IScheduleItemFields
{
    DateOnly Date { get; }
    string ProgramName { get; }
    TimeOnly StartTime { get; }
    TimeOnly? EndTime { get; }
}

internal static class ScheduleItemRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : IScheduleItemFields
    {
        validator.RuleFor(x => x.Date).NotEmpty();
        validator.RuleFor(x => x.ProgramName).NotEmpty().MaximumLength(200);

        validator.RuleFor(x => x.EndTime)
            .GreaterThan(x => x.StartTime)
            .WithMessage("A programme cannot end before it starts.")
            .When(x => x.EndTime.HasValue);
    }
}

internal static class EventForEditing
{
    /// <summary>The event, if this caller may edit it in this Samaaj - else the reason it is refused.</summary>
    public static async Task<(SamaajEvent? Event, Error? Refusal)> FindAsync(
        IEventRepository events,
        Guid eventId,
        IAreaScope areaScope,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        var samaajEvent = await events.GetByIdAsync(eventId, cancellationToken);

        // The write re-checks the tenant itself rather than trusting the query
        // filter alone (root CLAUDE.md section 6).
        if (samaajEvent is null
            || (tenantContext.TenantId is { } tenantId && samaajEvent.TenantId != tenantId)
            || !areaScope.MayAdminister(samaajEvent.LocalityId))
        {
            return (null, Error.NotFound("Event.NotFound", "No such event in this Samaaj."));
        }

        if (samaajEvent.Status == EventStatus.Cancelled)
        {
            return (null, Error.Conflict("Event.Cancelled", "A cancelled event's schedule cannot be changed."));
        }

        return (samaajEvent, null);
    }
}

// ---- Add -------------------------------------------------------------------

/// <summary>Adds one programme to an event's schedule.</summary>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record AddEventScheduleItemCommand(
    Guid EventId,
    DateOnly Date,
    string ProgramName,
    TimeOnly StartTime,
    TimeOnly? EndTime) : ICommand<EventResponse>, IScheduleItemFields;

public sealed class AddEventScheduleItemCommandValidator : AbstractValidator<AddEventScheduleItemCommand>
{
    public AddEventScheduleItemCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        ScheduleItemRules.Apply(this);
    }
}

public sealed class AddEventScheduleItemCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<AddEventScheduleItemCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        AddEventScheduleItemCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var (samaajEvent, refusal) = await EventForEditing.FindAsync(
            events, command.EventId, areaScope, tenantContext, cancellationToken);

        if (samaajEvent is null)
        {
            return Result.Failure<EventResponse>(refusal!);
        }

        var now = clock.UtcNow;

        if (samaajEvent.AddScheduleItem(command.Date, command.ProgramName, command.StartTime, command.EndTime, now) is null)
        {
            return Result.Failure<EventResponse>(Error.Conflict(
                "Event.ScheduleFull",
                $"An event's schedule holds at most {SamaajEvent.MaxScheduleItems} programmes."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(samaajEvent.ToResponse(memberId, now));
    }
}

// ---- Update ----------------------------------------------------------------

/// <summary>Corrects one programme on an event's schedule.</summary>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record UpdateEventScheduleItemCommand(
    Guid EventId,
    Guid ItemId,
    DateOnly Date,
    string ProgramName,
    TimeOnly StartTime,
    TimeOnly? EndTime) : ICommand<EventResponse>, IScheduleItemFields;

public sealed class UpdateEventScheduleItemCommandValidator : AbstractValidator<UpdateEventScheduleItemCommand>
{
    public UpdateEventScheduleItemCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
        ScheduleItemRules.Apply(this);
    }
}

public sealed class UpdateEventScheduleItemCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateEventScheduleItemCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        UpdateEventScheduleItemCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var (samaajEvent, refusal) = await EventForEditing.FindAsync(
            events, command.EventId, areaScope, tenantContext, cancellationToken);

        if (samaajEvent is null)
        {
            return Result.Failure<EventResponse>(refusal!);
        }

        var now = clock.UtcNow;

        if (!samaajEvent.UpdateScheduleItem(
                command.ItemId, command.Date, command.ProgramName, command.StartTime, command.EndTime, now))
        {
            return Result.Failure<EventResponse>(Error.NotFound(
                "Event.ScheduleItemNotFound", "That programme is not on this event's schedule."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(samaajEvent.ToResponse(memberId, now));
    }
}

// ---- Remove ----------------------------------------------------------------

/// <summary>Takes one programme off an event's schedule.</summary>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record RemoveEventScheduleItemCommand(Guid EventId, Guid ItemId) : ICommand<EventResponse>;

public sealed class RemoveEventScheduleItemCommandValidator : AbstractValidator<RemoveEventScheduleItemCommand>
{
    public RemoveEventScheduleItemCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.ItemId).NotEmpty();
    }
}

public sealed class RemoveEventScheduleItemCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IAreaScope areaScope,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveEventScheduleItemCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        RemoveEventScheduleItemCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var (samaajEvent, refusal) = await EventForEditing.FindAsync(
            events, command.EventId, areaScope, tenantContext, cancellationToken);

        if (samaajEvent is null)
        {
            return Result.Failure<EventResponse>(refusal!);
        }

        var now = clock.UtcNow;

        if (!samaajEvent.RemoveScheduleItem(command.ItemId, now))
        {
            return Result.Failure<EventResponse>(Error.NotFound(
                "Event.ScheduleItemNotFound", "That programme is not on this event's schedule."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(samaajEvent.ToResponse(memberId, now));
    }
}
