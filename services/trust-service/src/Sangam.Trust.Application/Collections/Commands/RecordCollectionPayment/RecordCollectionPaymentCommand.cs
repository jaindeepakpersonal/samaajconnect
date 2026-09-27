using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Collections.Commands.RecordCollectionPayment;

/// <summary>Settles a due collection - a donation pledge, or a Boli result owed to this Trust.</summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record RecordCollectionPaymentCommand(Guid CollectionId, string Mode) : ICommand<CollectionResponse>;

public sealed class RecordCollectionPaymentCommandValidator : AbstractValidator<RecordCollectionPaymentCommand>
{
    public RecordCollectionPaymentCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
        RuleFor(x => x.Mode).NotEmpty().MaximumLength(50);
    }
}

public sealed class RecordCollectionPaymentCommandHandler(
    ICollectionRepository collections,
    IDonationRepository donations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RecordCollectionPaymentCommand, Result<CollectionResponse>>
{
    public async Task<Result<CollectionResponse>> Handle(
        RecordCollectionPaymentCommand command, CancellationToken cancellationToken)
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

        if (collection.Status == Domain.Collections.CollectionStatus.Collected)
        {
            return Result.Failure<CollectionResponse>(Error.Conflict(
                "Collection.AlreadySettled", "This collection has already been settled."));
        }

        collection.Settle(command.Mode, actorId, clock.UtcNow);

        // A pledge paid from the collections ledger is a donation collected:
        // it gets its receipt number here, exactly as if it had been marked
        // collected on the donations list, so the two never disagree.
        if (collection.SourceType == Domain.Collections.CollectionSourceType.Donation)
        {
            var donation = await donations.GetByIdAsync(collection.SourceId, cancellationToken);

            if (donation is { Status: Domain.Donations.DonationStatus.Pledged })
            {
                var receiptNumber = await donations.ReserveNextReceiptNumberAsync(donation.TrustId, cancellationToken);

                donation.Collect(receiptNumber, actorId, clock.UtcNow);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(collection.ToResponse());
    }
}
