using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Donations.Commands.MarkDonationCollected;

/// <summary>
/// Settles a pledged donation: assigns the Trust's next receipt number and
/// marks it Collected. 409 on a donation that is not Pledged - a correction
/// to an already-collected donation is a distinct, audited workflow this
/// phase does not build, the same stance <c>boli-service</c> takes on
/// correcting a published result.
/// </summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record MarkDonationCollectedCommand(Guid DonationId) : ICommand<DonationResponse>;

public sealed class MarkDonationCollectedCommandValidator : AbstractValidator<MarkDonationCollectedCommand>
{
    public MarkDonationCollectedCommandValidator() => RuleFor(x => x.DonationId).NotEmpty();
}

public sealed class MarkDonationCollectedCommandHandler(
    IDonationRepository donations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<MarkDonationCollectedCommand, Result<DonationResponse>>
{
    public async Task<Result<DonationResponse>> Handle(
        MarkDonationCollectedCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<DonationResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var donation = await donations.GetByIdAsync(command.DonationId, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<DonationResponse>(
                Error.NotFound("Donation.NotFound", "No such donation in this Samaaj."));
        }

        if (donation.Status == Domain.Donations.DonationStatus.Collected)
        {
            return Result.Failure<DonationResponse>(Error.Conflict(
                "Donation.AlreadyCollected", "This donation has already been collected."));
        }

        // Reserved before Collect() so the number assigned is the one this
        // donation actually gets - see IDonationRepository.ReserveNextReceiptNumberAsync
        // for why this is safe under two collection requests racing each other.
        var receiptNumber = await donations.ReserveNextReceiptNumberAsync(donation.TrustId, cancellationToken);

        donation.Collect(receiptNumber, actorId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(donation.ToResponse());
    }
}
