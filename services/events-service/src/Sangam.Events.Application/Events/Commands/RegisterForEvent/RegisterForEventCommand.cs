using FluentValidation;
using MediatR;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;
using Sangam.Events.Domain.Events;

namespace Sangam.Events.Application.Events.Commands.RegisterForEvent;

/// <summary>
/// RSVPs to an event, or joins its waitlist when it is full.
/// </summary>
/// <remarks>
/// One command for both, because from the member's side it is one action: the
/// wireframe's button says "RSVP — I'm Going" on an open event and "Join
/// Waitlist" on a full one, and which they get depends on the room at that
/// moment rather than on which button they pressed. Two endpoints would let a
/// caller ask for a place on a full event and be told no, which is a worse
/// answer than being put in the queue.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record RegisterForEventCommand(Guid EventId) : ICommand<RegistrationResponse>;

public sealed class RegisterForEventCommandValidator : AbstractValidator<RegisterForEventCommand>
{
    public RegisterForEventCommandValidator() => RuleFor(x => x.EventId).NotEmpty();
}

public sealed class RegisterForEventCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RegisterForEventCommand, Result<RegistrationResponse>>
{
    public async Task<Result<RegistrationResponse>> Handle(
        RegisterForEventCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<RegistrationResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await events.GetByIdAsync(command.EventId, cancellationToken);

        if (found is null
            || (tenantContext.TenantId is { } tenantId && found.TenantId != tenantId)
            || found.Status == EventStatus.Draft)
        {
            // A draft is not an event anybody has been told about, so a member
            // reaching one has guessed its id. "Not found" for the same reason
            // an unapproved timeline post is.
            return Result.Failure<RegistrationResponse>(
                Error.NotFound("Event.NotFound", "No such event in this Samaaj."));
        }

        if (found.Status == EventStatus.Cancelled)
        {
            return Result.Failure<RegistrationResponse>(Error.Conflict(
                "Event.Cancelled", "This event has been cancelled."));
        }

        if (!found.RegistrationEnabled)
        {
            return Result.Failure<RegistrationResponse>(Error.Conflict(
                "Event.RegistrationClosed", "This event does not take registrations."));
        }

        if (found.StartAt <= clock.UtcNow)
        {
            return Result.Failure<RegistrationResponse>(Error.Conflict(
                "Event.AlreadyStarted", "This event has already started."));
        }

        var registration = found.Register(memberId, clock.UtcNow);

        if (registration is null)
        {
            var existing = found.FindRegistration(memberId);

            // Already in. Reported as success with what they hold, so
            // registering twice looks like registering once.
            return existing is null
                ? Result.Failure<RegistrationResponse>(Error.Conflict(
                    "Event.RegistrationClosed", "This event does not take registrations."))
                : Result.Success(Describe(found, existing));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(Describe(found, registration));
    }

    /// <summary>
    /// Position is where they stand in the waitlist queue, and 0 for a
    /// confirmed place. A member told only "Waitlisted" has been told very
    /// little; "third in the queue" is the thing they actually want.
    /// </summary>
    private static RegistrationResponse Describe(SamaajEvent found, EventRegistration registration)
    {
        var position = registration.Status == RegistrationStatus.Waitlisted
            ? found.Registrations
                .Where(r => r.Status == RegistrationStatus.Waitlisted
                    && r.RegisteredAt <= registration.RegisteredAt)
                .Count()
            : 0;

        return new RegistrationResponse(found.Id, registration.Status.ToString(), position);
    }
}
