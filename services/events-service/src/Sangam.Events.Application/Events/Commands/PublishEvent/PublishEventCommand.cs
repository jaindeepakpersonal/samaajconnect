using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;
using Sangam.Events.Domain.Events;

namespace Sangam.Events.Application.Events.Commands.PublishEvent;

/// <summary>Tells the Samaaj about an event that was a draft.</summary>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record PublishEventCommand(Guid EventId) : ICommand<EventResponse>;

public sealed class PublishEventCommandValidator : AbstractValidator<PublishEventCommand>
{
    public PublishEventCommandValidator() => RuleFor(x => x.EventId).NotEmpty();
}

public sealed class PublishEventCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<PublishEventCommandHandler> logger)
    : IRequestHandler<PublishEventCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        PublishEventCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<EventResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await events.GetByIdAsync(command.EventId, cancellationToken);

        if (found is null
            || (tenantContext.TenantId is { } tenantId && found.TenantId != tenantId))
        {
            return Result.Failure<EventResponse>(
                Error.NotFound("Event.NotFound", "No such event in this Samaaj."));
        }

        if (found.Status == EventStatus.Cancelled)
        {
            return Result.Failure<EventResponse>(Error.Conflict(
                "Event.Cancelled",
                "This event was cancelled. Create a new one rather than reviving it - "
                + "the people who were told it was off will not be told again."));
        }

        if (!found.Publish(clock.UtcNow))
        {
            // Already published. Reported as success with the event as it
            // stands: two organisers reaching for the same button is not a
            // conflict worth an error, and re-announcing it would be worse.
            return Result.Success(found.ToResponse(memberId));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Event {EventId} published by {MemberId}", found.Id, memberId);

        return Result.Success(found.ToResponse(memberId));
    }
}
