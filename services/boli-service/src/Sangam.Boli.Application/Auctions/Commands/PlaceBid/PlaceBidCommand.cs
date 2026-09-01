using FluentValidation;
using MediatR;
using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Application.Common;
using Sangam.Boli.Application.Security;
using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Application.Auctions.Commands.PlaceBid;

/// <summary>
/// Places one bid on one Boli.
/// </summary>
/// <remarks>
/// The busiest write path in this service, and the one with a real concurrency
/// requirement. The mechanism is in <see cref="IBoliRepository.LockForBiddingAsync"/>
/// and this service's own <c>CLAUDE.md</c>; what matters here is that the read of
/// the current highest and the write of the new bid happen **inside** the lock,
/// so nothing can slip between them.
///
/// Being outbid is not an error. If the amount does not clear the bar the
/// command succeeds with <c>Accepted: false</c> and the number the bidder now
/// needs, because somebody who was outbid while their form was open has done
/// nothing wrong and a 409 would tell them off for being slow.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record PlaceBidCommand(Guid BoliId, long Amount) : ICommand<PlaceBidResponse>;

public sealed class PlaceBidCommandValidator : AbstractValidator<PlaceBidCommand>
{
    public PlaceBidCommandValidator()
    {
        RuleFor(x => x.BoliId).NotEmpty();

        // The floor and the increment are the Boli's business and are checked in
        // the handler against the live highest. What is checked here is only that
        // the number is a plausible amount of money at all.
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithMessage("A bid must be more than nothing.");
    }
}

public sealed class PlaceBidCommandHandler(
    IBoliRepository boli,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<PlaceBidCommand, Result<PlaceBidResponse>>
{
    public async Task<Result<PlaceBidResponse>> Handle(
        PlaceBidCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<PlaceBidResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        // Everything from here to the commit is under a row lock on this Boli.
        var lot = await boli.LockForBiddingAsync(command.BoliId, cancellationToken);

        if (lot is null || (tenantContext.TenantId is { } tenantId && lot.TenantId != tenantId))
        {
            // The IDOR guard (root CLAUDE.md section 6): the query filter is not
            // relied on alone for a write path.
            return Result.Failure<PlaceBidResponse>(
                Error.NotFound("Boli.NotFound", "No such Boli in this Samaaj."));
        }

        var now = clock.UtcNow;

        if (!lot.AcceptsBids(now))
        {
            return Result.Failure<PlaceBidResponse>(Error.Conflict(
                "Boli.NotOpen", "This Boli is not taking bids."));
        }

        var highest = await boli.HighestAmountAsync(lot.Id, cancellationToken);

        if (!lot.IsAcceptable(command.Amount, highest))
        {
            // Success with Accepted: false. See the remarks above.
            return Result.Success(new PlaceBidResponse(
                lot.Id,
                BidId: null,
                Accepted: false,
                Reason: highest is null
                    ? "The first bid has to meet the starting amount."
                    : "Somebody has bid at least this much already.",
                HighestAmount: highest,
                MinimumNextBid: lot.MinimumNextBid(highest)));
        }

        var bid = Bid.Place(lot.TenantId, lot.Id, memberId, command.Amount, now);

        boli.AddBid(bid);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PlaceBidResponse(
            lot.Id,
            bid.Id,
            Accepted: true,
            Reason: null,
            HighestAmount: command.Amount,
            MinimumNextBid: lot.MinimumNextBid(command.Amount)));
    }
}
