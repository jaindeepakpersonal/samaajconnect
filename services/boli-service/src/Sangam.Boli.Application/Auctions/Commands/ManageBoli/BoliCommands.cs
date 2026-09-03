using FluentValidation;
using MediatR;
using Sangam.Boli.Application.Abstractions;
using Sangam.Boli.Application.Common;
using Sangam.Boli.Application.Security;
using Sangam.Boli.Domain.Auctions;

namespace Sangam.Boli.Application.Auctions.Commands.ManageBoli;

// ---- Open a Boli -------------------------------------------------------------

/// <summary>
/// Creates a Boli under an occasion and starts its bidding window.
/// </summary>
/// <remarks>
/// The endpoint is called "open" because that is what a Samaaj is doing, but the
/// Boli is created <c>Scheduled</c> and then started, so the two-step shape the
/// domain has stays visible. A Boli whose <c>StartAt</c> is in the future is
/// open in status and not yet taking bids, which is exactly the state
/// <c>AcceptsBids</c> exists to describe.
/// </remarks>
[RequiresPermission(PermissionKeys.BoliManage)]
public sealed record OpenBoliCommand(
    Guid OccasionId,
    Guid BoliTypeId,
    string Title,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    long StartingAmount,
    long MinIncrement,
    string? EligibilityRule,

    /// <summary>Seconds a closing bid pushes the end out by. 0 is off.</summary>
    int AutoExtendSeconds = 0) : ICommand<BoliResponse>;

public sealed class OpenBoliCommandValidator : AbstractValidator<OpenBoliCommand>
{
    public OpenBoliCommandValidator()
    {
        RuleFor(x => x.OccasionId).NotEmpty();
        RuleFor(x => x.BoliTypeId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EligibilityRule).MaximumLength(500);

        RuleFor(x => x.EndAt)
            .GreaterThan(x => x.StartAt)
            .WithMessage("A Boli has to close after it opens.");

        RuleFor(x => x.StartingAmount)
            .GreaterThan(0)
            .WithMessage("A Boli has to start somewhere above nothing.");

        // An increment of zero would let a Boli be won a paisa at a time, which
        // in a room full of people is a queue rather than bidding.
        RuleFor(x => x.MinIncrement)
            .GreaterThan(0)
            .WithMessage("The minimum increment has to be more than nothing.");

        // Zero is off, which is the default and stays valid. A negative one is
        // a mistake rather than a way of saying off, and an hour is long enough
        // that a Boli set to it would extend on essentially every bid - which
        // is not anti-sniping, it is an auction that never ends.
        RuleFor(x => x.AutoExtendSeconds)
            .InclusiveBetween(0, 3600)
            .WithMessage(
                "The auto-extend window is in seconds, from 0 (off) to 3600. "
                + "A minute or two is the usual choice.");
    }
}

public sealed class OpenBoliCommandHandler(
    IOccasionRepository occasions,
    IBoliRepository boli,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<OpenBoliCommand, Result<BoliResponse>>
{
    public async Task<Result<BoliResponse>> Handle(
        OpenBoliCommand command, CancellationToken cancellationToken)
    {
        var occasion = await occasions.GetByIdAsync(command.OccasionId, cancellationToken);

        if (occasion is null
            || (tenantContext.TenantId is { } tenantId && occasion.TenantId != tenantId))
        {
            return Result.Failure<BoliResponse>(
                Error.NotFound("Occasion.NotFound", "No such occasion in this Samaaj."));
        }

        var type = occasion.FindType(command.BoliTypeId);

        if (type is null)
        {
            return Result.Failure<BoliResponse>(Error.NotFound(
                "BoliType.NotFound", "That Boli type does not belong to this occasion."));
        }

        if (occasion.Status == OccasionStatus.Closed)
        {
            return Result.Failure<BoliResponse>(Error.Conflict(
                "Occasion.Closed", "This occasion is closed."));
        }

        var lot = Domain.Auctions.Boli.Open(
            occasion.TenantId,
            occasion.Id,
            type.Id,
            command.Title,
            command.StartAt,
            command.EndAt,
            command.StartingAmount,
            command.MinIncrement,
            command.EligibilityRule,
            clock.UtcNow,
            command.AutoExtendSeconds);

        lot.Start();

        boli.Add(lot);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BoliMappings.ToResponse(
            lot, type.Name, clock.UtcNow, highest: null, highestBidderIsMe: false, bidCount: 0));
    }
}

// ---- Close a Boli ------------------------------------------------------------

/// <summary>Ends the bidding. Idempotent.</summary>
[RequiresPermission(PermissionKeys.BoliManage)]
public sealed record CloseBoliCommand(Guid BoliId) : ICommand<BoliResponse>;

public sealed class CloseBoliCommandValidator : AbstractValidator<CloseBoliCommand>
{
    public CloseBoliCommandValidator() => RuleFor(x => x.BoliId).NotEmpty();
}

public sealed class CloseBoliCommandHandler(
    IOccasionRepository occasions,
    IBoliRepository boli,
    IUnitOfWork unitOfWork,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<CloseBoliCommand, Result<BoliResponse>>
{
    public async Task<Result<BoliResponse>> Handle(
        CloseBoliCommand command, CancellationToken cancellationToken)
    {
        // Locked rather than merely read: closing races the last bids by
        // definition, and a bid accepted after the close would be a bid nobody
        // could have known was too late.
        var lot = await boli.LockForBiddingAsync(command.BoliId, cancellationToken);

        if (lot is null || (tenantContext.TenantId is { } tenantId && lot.TenantId != tenantId))
        {
            return Result.Failure<BoliResponse>(
                Error.NotFound("Boli.NotFound", "No such Boli in this Samaaj."));
        }

        if (!lot.Close(clock.UtcNow))
        {
            return Result.Failure<BoliResponse>(Error.Conflict(
                "Boli.NotOpen", "This Boli was never opened for bidding."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await BoliMappings.DescribeAsync(
            lot, occasions, boli, currentMemberId: null, clock.UtcNow, cancellationToken);
    }
}

// ---- Record a result ---------------------------------------------------------

/// <summary>
/// Records who won, from the highest bid. Not announced until it is published.
/// </summary>
/// <remarks>
/// The winner is not a parameter. Taking one would let a recorded result name
/// somebody who never made the highest bid, and the bid history — which is
/// append-only — would sit beside it contradicting it. The Samaaj's own record of
/// what happened in the room is the bids.
/// </remarks>
[RequiresPermission(PermissionKeys.BoliManage)]
public sealed record RecordResultCommand(Guid BoliId) : ICommand<BoliResultResponse>;

public sealed class RecordResultCommandValidator : AbstractValidator<RecordResultCommand>
{
    public RecordResultCommandValidator() => RuleFor(x => x.BoliId).NotEmpty();
}

public sealed class RecordResultCommandHandler(
    IBoliRepository boli,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RecordResultCommand, Result<BoliResultResponse>>
{
    public async Task<Result<BoliResultResponse>> Handle(
        RecordResultCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<BoliResultResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var lot = await boli.GetByIdAsync(command.BoliId, cancellationToken);

        if (lot is null || (tenantContext.TenantId is { } tenantId && lot.TenantId != tenantId))
        {
            return Result.Failure<BoliResultResponse>(
                Error.NotFound("Boli.NotFound", "No such Boli in this Samaaj."));
        }

        if (lot.Status is not (BoliStatus.Closed or BoliStatus.ResultPublished))
        {
            return Result.Failure<BoliResultResponse>(Error.Conflict(
                "Boli.NotClosed", "Close the bidding before recording a result."));
        }

        var existing = await boli.GetResultAsync(lot.Id, cancellationToken);

        if (existing is not null)
        {
            // Idempotent while unpublished; refused once announced, because a
            // published result is fixed (SERVICES.md).
            return existing.IsPublished
                ? Result.Failure<BoliResultResponse>(Error.Conflict(
                    "Boli.ResultPublished",
                    "This result has been announced and cannot be recorded again."))
                : Result.Success(BoliMappings.ToResponse(existing, lot, actorId));
        }

        var winning = await boli.HighestBidAsync(lot.Id, cancellationToken);

        if (winning is null)
        {
            return Result.Failure<BoliResultResponse>(Error.Conflict(
                "Boli.NoBids", "Nobody bid on this Boli, so there is nothing to record."));
        }

        var result = BoliResult.Record(lot.TenantId, lot.Id, winning, actorId, clock.UtcNow);

        boli.AddResult(result);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BoliMappings.ToResponse(result, lot, actorId));
    }
}

// ---- Publish a result --------------------------------------------------------

/// <summary>
/// Announces a recorded result to the Samaaj. Idempotent and irreversible.
/// </summary>
/// <remarks>
/// Its own permission (<see cref="PermissionKeys.BoliPublishResults"/>), because
/// it is the step that cannot be taken back.
/// </remarks>
[RequiresPermission(PermissionKeys.BoliPublishResults)]
public sealed record PublishResultCommand(Guid BoliId) : ICommand<BoliResultResponse>;

public sealed class PublishResultCommandValidator : AbstractValidator<PublishResultCommand>
{
    public PublishResultCommandValidator() => RuleFor(x => x.BoliId).NotEmpty();
}

public sealed class PublishResultCommandHandler(
    IBoliRepository boli,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<PublishResultCommand, Result<BoliResultResponse>>
{
    public async Task<Result<BoliResultResponse>> Handle(
        PublishResultCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<BoliResultResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var lot = await boli.GetByIdAsync(command.BoliId, cancellationToken);

        if (lot is null || (tenantContext.TenantId is { } tenantId && lot.TenantId != tenantId))
        {
            return Result.Failure<BoliResultResponse>(
                Error.NotFound("Boli.NotFound", "No such Boli in this Samaaj."));
        }

        var result = await boli.GetResultAsync(lot.Id, cancellationToken);

        if (result is null)
        {
            return Result.Failure<BoliResultResponse>(Error.Conflict(
                "Boli.NoResult", "Record the result before announcing it."));
        }

        if (result.IsPublished)
        {
            // Already announced. Success, not a conflict: a retried request must
            // be safe, and nothing about the second one is wrong.
            return Result.Success(BoliMappings.ToResponse(result, lot, actorId));
        }

        result.Publish(actorId, clock.UtcNow);
        lot.MarkPublished(result.WinningMemberId, result.Amount, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(BoliMappings.ToResponse(result, lot, actorId));
    }
}
