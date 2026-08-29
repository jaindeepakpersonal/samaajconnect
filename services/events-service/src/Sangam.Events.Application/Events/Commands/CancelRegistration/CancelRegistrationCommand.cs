using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Sangam.Events.Application.Abstractions;
using Sangam.Events.Application.Common;
using Sangam.Events.Application.Security;

namespace Sangam.Events.Application.Events.Commands.CancelRegistration;

/// <summary>
/// Gives up a place, or leaves the waitlist.
/// </summary>
/// <remarks>
/// Giving up a confirmed place promotes whoever has waited longest. That
/// promotion is the whole reason a waitlist is worth having: without it the
/// queue is a list nobody ever comes off, which is worse than not offering one.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record CancelRegistrationCommand(Guid EventId)
    : ICommand<CancelRegistrationResponse>;

public sealed class CancelRegistrationCommandValidator
    : AbstractValidator<CancelRegistrationCommand>
{
    public CancelRegistrationCommandValidator() => RuleFor(x => x.EventId).NotEmpty();
}

public sealed class CancelRegistrationCommandHandler(
    IEventRepository events,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock,
    ILogger<CancelRegistrationCommandHandler> logger)
    : IRequestHandler<CancelRegistrationCommand, Result<CancelRegistrationResponse>>
{
    public async Task<Result<CancelRegistrationResponse>> Handle(
        CancelRegistrationCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<CancelRegistrationResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await events.GetByIdAsync(command.EventId, cancellationToken);

        if (found is null
            || (tenantContext.TenantId is { } tenantId && found.TenantId != tenantId))
        {
            return Result.Failure<CancelRegistrationResponse>(
                Error.NotFound("Event.NotFound", "No such event in this Samaaj."));
        }

        var outcome = found.CancelRegistration(memberId, clock.UtcNow);

        if (outcome.Cancelled)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (outcome.PromotedMemberId is { } promoted)
            {
                logger.LogInformation(
                    "Event {EventId}: {MemberId} came off the waitlist", found.Id, promoted);
            }
        }

        // Cancelling something you never had reports success. Saying "you were
        // not registered" would be a distinction nobody needs and one that
        // tells a caller whether a given member is on the list.
        return Result.Success(new CancelRegistrationResponse(
            found.Id, outcome.Cancelled, outcome.PromotedMemberId));
    }
}
