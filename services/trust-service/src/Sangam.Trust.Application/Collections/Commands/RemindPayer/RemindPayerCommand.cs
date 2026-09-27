using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Collections;

namespace Sangam.Trust.Application.Collections.Commands.RemindPayer;

/// <summary>
/// Reminds a member that an amount is still due to the Trust - a pledge they
/// made, or a Boli they won. The reminder is an in-app notification raised by
/// audit-notification-service from <c>trust.collection.reminded.v1</c>.
/// </summary>
/// <remarks>
/// Refused (409) when it is already paid, when the payer is not a member and
/// so has no inbox to put it in, and within a day of the last reminder - an
/// officer pressing twice should not send twice.
/// </remarks>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record RemindPayerCommand(Guid CollectionId) : ICommand<CollectionResponse>;

public sealed class RemindPayerCommandValidator : AbstractValidator<RemindPayerCommand>
{
    public RemindPayerCommandValidator() => RuleFor(x => x.CollectionId).NotEmpty();
}

public sealed class RemindPayerCommandHandler(
    ICollectionRepository collections,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RemindPayerCommand, Result<CollectionResponse>>
{
    public async Task<Result<CollectionResponse>> Handle(
        RemindPayerCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<CollectionResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var collection = await collections.GetByIdAsync(command.CollectionId, cancellationToken);

        if (collection is null)
        {
            return Result.Failure<CollectionResponse>(
                Error.NotFound("Collection.NotFound", "No such collection in this Samaaj."));
        }

        switch (collection.Remind(actorId, clock.UtcNow))
        {
            case ReminderOutcome.NotDue:
                return Result.Failure<CollectionResponse>(Error.Conflict(
                    "Collection.AlreadySettled", "This has already been paid."));

            case ReminderOutcome.NotAMember:
                return Result.Failure<CollectionResponse>(Error.Conflict(
                    "Collection.PayerNotAMember",
                    "The donor has no account to send a reminder to. Call them instead."));

            case ReminderOutcome.TooSoon:
                return Result.Failure<CollectionResponse>(Error.Conflict(
                    "Collection.RemindedRecently", "They were reminded in the last day."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(collection.ToResponse());
    }
}
