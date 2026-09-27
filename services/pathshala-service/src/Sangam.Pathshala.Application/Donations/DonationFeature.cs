using FluentValidation;
using MediatR;
using Sangam.Pathshala.Application.Abstractions;
using Sangam.Pathshala.Application.Common;
using Sangam.Pathshala.Application.Security;
using Sangam.Pathshala.Domain.Donations;

namespace Sangam.Pathshala.Application.Donations;

// A Pathshala's own donation ledger (confirmed with the user, 2026-09-26):
// record-keeping only, nothing here takes a payment. Recording, voiding and
// reading it are whoever runs the Pathshala - Pathshala.Manage, and an Area
// Admin for their own locality's. Teachers do not see it: who gave how much is
// the administrators' to keep.

public sealed record DonationResponse(
    Guid Id,
    int ReceiptNumber,
    string DonorName,
    Guid? DonorMemberId,

    /// <summary>Integer paise.</summary>
    long Amount,
    DateOnly DonatedOn,
    string Mode,
    string? Purpose,
    Guid RecordedBy,
    DateTimeOffset RecordedAt,
    bool IsVoid,
    string? VoidReason);

/// <summary>The ledger, newest receipt first, and its total with voided entries left out.</summary>
public sealed record DonationLedgerResponse(IReadOnlyList<DonationResponse> Donations, long TotalAmount);

// ---- Record ------------------------------------------------------------------

[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record RecordDonationCommand(
    Guid PathshalaId,
    string DonorName,

    /// <summary>A string, not a Guid - see CreatePathshalaCommand. The donor's member id, when they are one.</summary>
    string? DonorMemberId,

    /// <summary>Integer paise, above zero.</summary>
    long Amount,
    DateOnly DonatedOn,
    string Mode,
    string? Purpose) : ICommand<DonationResponse>;

public sealed class RecordDonationCommandValidator : AbstractValidator<RecordDonationCommand>
{
    public RecordDonationCommandValidator(IDateTimeProvider clock)
    {
        RuleFor(x => x.PathshalaId).NotEmpty();
        RuleFor(x => x.DonorName).NotEmpty().MaximumLength(200);

        RuleFor(x => x.DonorMemberId)
            .Must(v => string.IsNullOrWhiteSpace(v) || Guid.TryParse(v, out _))
            .WithMessage("That member is not valid.");

        // A crore of rupees is a lot for one Pathshala receipt, and a typo with
        // three extra zeros is more likely than a gift of that size.
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1_00_00_000_00L);

        RuleFor(x => x.DonatedOn)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime.AddDays(1)))
            .WithMessage("A donation cannot be dated in the future.");

        RuleFor(x => x.Mode)
            .Must(m => Enum.TryParse<DonationMode>(m, ignoreCase: true, out _))
            .WithMessage($"Mode must be one of: {string.Join(", ", Enum.GetNames<DonationMode>())}.");

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

        // The courtesy: the next number. Two donations recorded at the same
        // instant would both be given it, and the unique index on
        // (Pathshala, ReceiptNumber) refuses the second - a failed save, not two
        // receipts with one number.
        var receipt = await donations.NextReceiptNumberAsync(pathshala!.Id, cancellationToken);

        var donation = PathshalaDonation.Record(
            pathshala.TenantId,
            pathshala.Id,
            receipt,
            command.DonorName,
            string.IsNullOrWhiteSpace(command.DonorMemberId) ? null : Guid.Parse(command.DonorMemberId),
            command.Amount,
            command.DonatedOn,
            Enum.Parse<DonationMode>(command.Mode, ignoreCase: true),
            command.Purpose,
            currentUser.UserId ?? Guid.Empty,
            clock.UtcNow);

        donations.Add(donation);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(donation.ToResponse());
    }
}

// ---- Void --------------------------------------------------------------------

/// <summary>
/// Takes a mistaken entry out of the totals, with a reason. Never a delete: a
/// receipt number that was handed to somebody keeps pointing at something.
/// </summary>
[RequiresPermission(PermissionKeys.PathshalaManage)]
public sealed record VoidDonationCommand(Guid DonationId, string Reason) : ICommand<DonationResponse>;

public sealed class VoidDonationCommandValidator : AbstractValidator<VoidDonationCommand>
{
    public VoidDonationCommandValidator()
    {
        RuleFor(x => x.DonationId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
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
    private static readonly Error NoSuchDonation =
        Error.NotFound("Donation.NotFound", "No such donation in this Samaaj.");

    public async Task<Result<DonationResponse>> Handle(
        VoidDonationCommand command, CancellationToken cancellationToken)
    {
        var donation = await donations.GetByIdAsync(command.DonationId, cancellationToken);

        if (donation is null || (tenantContext.TenantId is { } tenantId && donation.TenantId != tenantId))
        {
            return Result.Failure<DonationResponse>(NoSuchDonation);
        }

        var pathshala = await pathshalas.GetByIdAsync(donation.PathshalaId, cancellationToken);

        if (!pathshala.IsAdministeredBy(areaScope))
        {
            return Result.Failure<DonationResponse>(NoSuchDonation);
        }

        if (!donation.Void(command.Reason, currentUser.UserId ?? Guid.Empty, clock.UtcNow))
        {
            return Result.Failure<DonationResponse>(Error.Conflict(
                "Donation.AlreadyVoid", "This donation has already been voided."));
        }

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

        return Result.Success(new DonationLedgerResponse(
            [.. ledger.Select(d => d.ToResponse())],
            ledger.Where(d => !d.IsVoid).Sum(d => d.Amount)));
    }
}

internal static class DonationMappings
{
    public static DonationResponse ToResponse(this PathshalaDonation donation) =>
        new(donation.Id,
            donation.ReceiptNumber,
            donation.DonorName,
            donation.DonorMemberId,
            donation.Amount,
            donation.DonatedOn,
            donation.Mode.ToString(),
            donation.Purpose,
            donation.RecordedBy,
            donation.RecordedAt,
            donation.IsVoid,
            donation.VoidReason);
}
