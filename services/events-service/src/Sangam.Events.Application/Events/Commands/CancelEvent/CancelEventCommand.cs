using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;

namespace Sangam.Events.Application.Events.Commands.CancelEvent;

/// <summary>
/// Calls an event off.
/// </summary>
/// <remarks>
/// Registrations are kept rather than deleted. People who were going need to be
/// told, and an attendee list that vanished with the event is one nobody can
/// notify.
/// </remarks>
[RequiresPermission(PermissionKeys.EventsPublish)]
public sealed record CancelEventCommand(Guid EventId, string? Reason) : ICommand<EventResponse>;

public sealed class CancelEventCommandValidator : AbstractValidator<CancelEventCommand>
{
    public CancelEventCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();

        // People rearranged their day around this. "Cancelled" with no
        // explanation is not an answer.
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("Say why. Members who were going are told this.")
            .MaximumLength(1000);
    }
}

public sealed class CancelEventCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<CancelEventCommandHandler> logger)
    : IRequestHandler<CancelEventCommand, Result<EventResponse>>
{
    public async Task<Result<EventResponse>> Handle(
        CancelEventCommand command,
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

        if (found.Cancel(command.Reason, clock.UtcNow))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "Event {EventId} cancelled by {MemberId}; {Count} registration(s) affected",
                found.Id,
                memberId,
                found.RegisteredCount + found.WaitlistedCount);
        }

        return Result.Success(found.ToResponse(memberId));
    }
}
