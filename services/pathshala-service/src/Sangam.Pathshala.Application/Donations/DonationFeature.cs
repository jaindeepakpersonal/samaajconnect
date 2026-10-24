using FluentValidation;
using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Donations;

namespace Sangam.Pathshala.Application.Donations;

// A Pathshala's own donation ledger (confirmed with the user, 2026-09-26):
// record-keeping only, nothing here takes a payment. Recording, correcting,
// marking paid, deleting and reading it are whoever runs the Pathshala -
// Pathshala.Manage, and an Area Admin for their own locality's. Teachers do not
// see it: who gave how much is the administrators' to keep.
//
// Since 2026-10-24 a donation may be announced and paid later (the user's
// requirement): recorded Pledged, with no receipt number until MarkDonationPaid
// issues one.

public sealed record DonationResponse(
    Guid Id,

    /// <summary>"Pledged" (announced, not yet paid) or "Paid".</summary>
    string Status,

    /// <summary>Null until the money is in.</summary>
    int? ReceiptNumber,
    string DonorName,
    Guid? DonorMemberId,

    /// <summary>Integer paise.</summary>
    long Amount,

    /// <summary>The day it was given or, for a pledge, announced.</summary>
    DateOnly DonatedOn,

    /// <summary>Null on a pledge.</summary>
    string? Mode,
    DateOnly? PaidOn,
    string? Purpose,
    Guid RecordedBy,
    DateTimeOffset RecordedAt,
    DateTimeOffset? UpdatedAt,
    bool IsVoid,
    string? VoidReason);

/// <summary>
/// The ledger, newest first. <c>TotalAmount</c> is what has been received;
/// <c>OutstandingAmount</c> is what has been announced and not yet paid. Both
/// leave deleted entries out.
/// </summary>
public sealed record DonationLedgerResponse(
    IReadOnlyList<DonationResponse> Donations, long TotalAmount, long OutstandingAmount);

// ---- Record ------------------------------------------------------------------

[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record RecordDonationCommand(
    Guid PathshalaId,
    string DonorName,

    /// <summary>A string, not a Guid - see CreatePathshalaCommand. The donor's member id, when they are one.</summary>
    string? DonorMemberId,

    /// <summary>Integer paise, above zero.</summary>
    long Amount,

    /// <summary>The day it was given or, for a pledge, announced.</summary>
    DateOnly DonatedOn,

    /// <summary>Required when <see cref="Paid"/>; ignored on a pledge.</summary>
    string? Mode,
    string? Purpose,

    /// <summary>False for a gift announced and still to be paid - no receipt yet.</summary>
    bool Paid = true) : ICommand<DonationResponse>;

public sealed class RecordDonationCommandValidator : AbstractValidator<RecordDonationCommand>
{
    public RecordDonationCommandValidator(IDateTimeProvider clock)
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        RuleFor(x => x.DonorName).NotEmpty().MaximumLength(200);

        RuleFor(x => x.DonorMemberId)
            .Must(v => string.IsNullOrWhiteSpace(v) || Guid.TryParse(v, out _))
            .WithMessage("That member is not valid.");

        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(DonationRules.MaximumAmount);

        RuleFor(x => x.DonatedOn)
            .LessThanOrEqualTo(_ => DonationRules.LatestDate(clock))
            .WithMessage("A donation cannot be dated in the future.");

        RuleFor(x => x.Mode)
            .Must(DonationRules.IsMode)
            .When(x => x.Paid)
            .WithMessage(DonationRules.ModeMessage);

        RuleFor(x => x.Purpose).MaximumLength(200);
    }
}

public sealed class RecordDonationCommandHandler(
    IPathshalaRepository pathshalas,
    IDonationRepository donations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<RecordDonationCommand, Result<DonationResponse>>
{
    public async Task<Result<DonationResponse>> Handle(
        RecordDonationCommand command, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(command.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId) || !pathshala.IsAdministeredBy(areaScope))
        {
            return Result.Failure<DonationResponse>(PathshalaAccess.NoSuchPathshala);
        }

        var donorMemberId = string.IsNullOrWhiteSpace(command.DonorMemberId) ? (Guid?)null : Guid.Parse(command.DonorMemberId);
        var recordedBy = currentUser.UserId ?? Guid.Empty;

        PathshalaDonation donation;

        if (command.Paid)
        {
            // The courtesy: the next number. Two donations recorded at the same
            // instant would both be given it, and the unique index on
            // (Pathshala, ReceiptNumber) refuses the second - a failed save, not
            // two receipts with one number.
            var receipt = await donations.NextReceiptNumberAsync(pathshala!.Id, cancellationToken);

            donation = PathshalaDonation.Record(
                pathshala.TenantId,
                pathshala.Id,
                receipt,
                command.DonorName,
                donorMemberId,
                command.Amount,
                command.DonatedOn,
                DonationRules.ParseMode(command.Mode!),
                command.Purpose,
                recordedBy,
                clock.UtcNow);
        }
        else
        {
            donation = PathshalaDonation.Pledge(
                pathshala!.TenantId,
                pathshala.Id,
                command.DonorName,
                donorMemberId,
                command.Amount,
                command.DonatedOn,
                command.Purpose,
                recordedBy,
                clock.UtcNow);
        }

        donations.Add(donation);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(donation.ToResponse());
    }
}

// ---- Correct -----------------------------------------------------------------

/// <summary>
/// Corrects an entry - donor, amount, dates, mode, purpose. A pledge stays a
/// pledge (marking it paid is its own act, because that issues a receipt), so
/// its mode and paid date are not asked for; a paid entry needs both.
/// </summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record UpdateDonationCommand(
    Guid DonationId,
    string DonorName,
    long Amount,
    DateOnly DonatedOn,
    string? Mode,
    DateOnly? PaidOn,
    string? Purpose) : ICommand<DonationResponse>;

public sealed class UpdateDonationCommandValidator : AbstractValidator<UpdateDonationCommand>
{
    public UpdateDonationCommandValidator(IDateTimeProvider clock)
    {
        RuleFor(x => x.DonationId).NotEmpty();
        RuleFor(x => x.DonorName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(DonationRules.MaximumAmount);

        RuleFor(x => x.DonatedOn)
            .LessThanOrEqualTo(_ => DonationRules.LatestDate(clock))
            .WithMessage("A donation cannot be dated in the future.");

        RuleFor(x => x.Mode)
            .Must(DonationRules.IsMode)
            .When(x => x.Mode is not null)
            .WithMessage(DonationRules.ModeMessage);

        RuleFor(x => x.PaidOn)
            .LessThanOrEqualTo(_ => DonationRules.LatestDate(clock))
            .When(x => x.PaidOn is not null)
            .WithMessage("A payment cannot be dated in the future.");

        RuleFor(x => x.PaidOn)
            .GreaterThanOrEqualTo(x => x.DonatedOn)
            .When(x => x.PaidOn is not null)
            .WithMessage("It cannot have been paid before it was announced.");

        RuleFor(x => x.Purpose).MaximumLength(200);
    }
}

public sealed class UpdateDonationCommandHandler(
    IPathshalaRepository pathshalas,
    IDonationRepository donations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateDonationCommand, Result<DonationResponse>>
{
    public async Task<Result<DonationResponse>> Handle(
        UpdateDonationCommand command, CancellationToken cancellationToken)
    {
        var donation = await DonationRules.FindAsync(
            command.DonationId, pathshalas, donations, tenantContext, areaScope, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<DonationResponse>(DonationRules.NoSuchDonation);
        }

        if (donation.IsVoid)
        {
            return Result.Failure<DonationResponse>(DonationRules.Deleted);
        }

        if (donation.Status == DonationStatus.Paid && (command.Mode is null || command.PaidOn is null))
        {
            return Result.Failure<DonationResponse>(Error.Validation(new Dictionary<string, string[]>
            {
                [command.Mode is null ? "mode" : "paidOn"] = ["A paid donation needs how and when it was paid."],
            }));
        }

        donation.Correct(
            command.DonorName,
            command.Amount,
            command.DonatedOn,
            command.Mode is null ? null : DonationRules.ParseMode(command.Mode),
            command.PaidOn,
            command.Purpose,
            currentUser.UserId ?? Guid.Empty,
            clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(donation.ToResponse());
    }
}

// ---- Mark paid ---------------------------------------------------------------

/// <summary>An announced donation was paid: issues its receipt number.</summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record MarkDonationPaidCommand(Guid DonationId, string Mode, DateOnly PaidOn) : ICommand<DonationResponse>;

public sealed class MarkDonationPaidCommandValidator : AbstractValidator<MarkDonationPaidCommand>
{
    public MarkDonationPaidCommandValidator(IDateTimeProvider clock)
    {
        RuleFor(x => x.DonationId).NotEmpty();
        RuleFor(x => x.Mode).Must(DonationRules.IsMode).WithMessage(DonationRules.ModeMessage);

        RuleFor(x => x.PaidOn)
            .LessThanOrEqualTo(_ => DonationRules.LatestDate(clock))
            .WithMessage("A payment cannot be dated in the future.");
    }
}

public sealed class MarkDonationPaidCommandHandler(
    IPathshalaRepository pathshalas,
    IDonationRepository donations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<MarkDonationPaidCommand, Result<DonationResponse>>
{
    public async Task<Result<DonationResponse>> Handle(
        MarkDonationPaidCommand command, CancellationToken cancellationToken)
    {
        var donation = await DonationRules.FindAsync(
            command.DonationId, pathshalas, donations, tenantContext, areaScope, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<DonationResponse>(DonationRules.NoSuchDonation);
        }

        if (donation.IsVoid)
        {
            return Result.Failure<DonationResponse>(DonationRules.Deleted);
        }

        if (donation.Status != DonationStatus.Pledged)
        {
            return Result.Failure<DonationResponse>(Error.Conflict(
                "Donation.AlreadyPaid", $"This donation has already been paid, on receipt {donation.ReceiptNumber}."));
        }

        if (command.PaidOn < donation.DonatedOn)
        {
            return Result.Failure<DonationResponse>(Error.Validation(new Dictionary<string, string[]>
            {
                ["paidOn"] = ["It cannot have been paid before it was announced."],
            }));
        }

        // The same courtesy, and the same index behind it, as recording one paid.
        var receipt = await donations.NextReceiptNumberAsync(donation.PathshalaId, cancellationToken);

        donation.MarkPaid(
            receipt, DonationRules.ParseMode(command.Mode), command.PaidOn, currentUser.UserId ?? Guid.Empty, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(donation.ToResponse());
    }
}

// ---- Delete ------------------------------------------------------------------

/// <summary>
/// Deletes an entry from the ledger: it leaves the totals and the ledger's
/// default view. The row stays, because a receipt number that was handed to
/// somebody keeps pointing at something - so once there is a receipt a reason is
/// required, and the number is never issued again. A pledge needs none.
/// </summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record VoidDonationCommand(Guid DonationId, string? Reason) : ICommand<DonationResponse>;

public sealed class VoidDonationCommandValidator : AbstractValidator<VoidDonationCommand>
{
    public VoidDonationCommandValidator()
    {
        RuleFor(x => x.DonationId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class VoidDonationCommandHandler(
    IPathshalaRepository pathshalas,
    IDonationRepository donations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IAreaScope areaScope,
    IDateTimeProvider clock)
    : IRequestHandler<VoidDonationCommand, Result<DonationResponse>>
{
    public async Task<Result<DonationResponse>> Handle(
        VoidDonationCommand command, CancellationToken cancellationToken)
    {
        var donation = await DonationRules.FindAsync(
            command.DonationId, pathshalas, donations, tenantContext, areaScope, cancellationToken);

        if (donation is null)
        {
            return Result.Failure<DonationResponse>(DonationRules.NoSuchDonation);
        }

        if (donation.IsVoid)
        {
            return Result.Failure<DonationResponse>(DonationRules.Deleted);
        }

        if (donation.ReceiptNumber is not null && string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure<DonationResponse>(Error.Validation(new Dictionary<string, string[]>
            {
                ["reason"] = [$"Receipt {donation.ReceiptNumber} has been issued, so say why it is being deleted."],
            }));
        }

        donation.Void(command.Reason, currentUser.UserId ?? Guid.Empty, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(donation.ToResponse());
    }
}

// ---- Read --------------------------------------------------------------------

[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record ListDonationsQuery(Guid PathshalaId) : IQuery<DonationLedgerResponse>;

public sealed class ListDonationsQueryHandler(
    IPathshalaRepository pathshalas,
    IDonationRepository donations,
    ITenantContext tenantContext,
    IAreaScope areaScope)
    : IRequestHandler<ListDonationsQuery, Result<DonationLedgerResponse>>
{
    public async Task<Result<DonationLedgerResponse>> Handle(
        ListDonationsQuery query, CancellationToken cancellationToken)
    {
        var pathshala = await pathshalas.GetByIdAsync(query.PathshalaId, cancellationToken);

        if (!pathshala.IsInTenant(tenantContext.TenantId) || !pathshala.IsAdministeredBy(areaScope))
        {
            return Result.Failure<DonationLedgerResponse>(PathshalaAccess.NoSuchPathshala);
        }

        var ledger = await donations.ListForPathshalaAsync(pathshala!.Id, cancellationToken);
        var standing = ledger.Where(d => !d.IsVoid).ToList();

        return Result.Success(new DonationLedgerResponse(
            [.. ledger.Select(d => d.ToResponse())],
            standing.Where(d => d.Status == DonationStatus.Paid).Sum(d => d.Amount),
            standing.Where(d => d.Status == DonationStatus.Pledged).Sum(d => d.Amount)));
    }
}

internal static class DonationRules
{
    /// <summary>
    /// A crore of rupees is a lot for one Pathshala receipt, and a typo with
    /// three extra zeros is more likely than a gift of that size.
    /// </summary>
    public const long MaximumAmount = 1_00_00_000_00L;

    public static readonly Error NoSuchDonation =
        Error.NotFound("Donation.NotFound", "No such donation in this Samaaj.");

    public static readonly Error Deleted =
        Error.Conflict("Donation.Deleted", "This donation has been deleted from the ledger.");

    public static readonly string ModeMessage =
        $"Mode must be one of: {string.Join(", ", Enum.GetNames<DonationMode>())}.";

    /// <summary>Today in UTC plus a day - the date is the treasurer's own, east of Greenwich.</summary>
    public static DateOnly LatestDate(IDateTimeProvider clock) =>
        DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddDays(1));

    public static bool IsMode(string? mode) => Enum.TryParse<DonationMode>(mode, ignoreCase: true, out _);

    public static DonationMode ParseMode(string mode) => Enum.Parse<DonationMode>(mode, ignoreCase: true);

    /// <summary>
    /// The donation, when it is in this Samaaj and the caller administers its
    /// Pathshala; null - one "not found" - otherwise.
    /// </summary>
    public static async Task<PathshalaDonation?> FindAsync(
        Guid donationId,
        IPathshalaRepository pathshalas,
        IDonationRepository donations,
        ITenantContext tenantContext,
        IAreaScope areaScope,
        CancellationToken cancellationToken)
    {
        var donation = await donations.GetByIdAsync(donationId, cancellationToken);

        if (donation is null || (tenantContext.TenantId is { } tenantId && donation.TenantId != tenantId))
        {
            return null;
        }

        var pathshala = await pathshalas.GetByIdAsync(donation.PathshalaId, cancellationToken);

        return pathshala.IsAdministeredBy(areaScope) ? donation : null;
    }
}

internal static class DonationMappings
{
    public static DonationResponse ToResponse(this PathshalaDonation donation) =>
        new(donation.Id,
            donation.Status.ToString(),
            donation.ReceiptNumber,
            donation.DonorName,
            donation.DonorMemberId,
            donation.Amount,
            donation.DonatedOn,
            donation.Mode?.ToString(),
            donation.PaidOn,
            donation.Purpose,
            donation.RecordedBy,
            donation.RecordedAt,
            donation.UpdatedAt,
            donation.IsVoid,
            donation.VoidReason);
}
