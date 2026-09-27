using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Donations;

namespace Sangam.Trust.Application.Donations.Commands.RecordDonation;

/// <summary>
/// Records a pledge against a Trust. Starts Pledged - see
/// <see cref="Donation.Collect"/> for the second step that actually settles it.
/// </summary>
/// <remarks>
/// <paramref name="DonorMemberId"/> is a string, not a <see cref="Guid"/> - the
/// same wire-binding defense <c>AddTrustMemberCommand</c> already uses for a
/// body-bound id. At least one of <paramref name="DonorMemberId"/> or
/// <paramref name="DonorName"/> must be present: a donor need not hold an
/// account, but a donation naming nobody at all is not a record of anything.
/// </remarks>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record RecordDonationCommand(
    Guid TrustId,
    string? DonorMemberId,
    string? DonorName,
    string? DonorPhone,
    long Amount,
    string Purpose,
    string Mode) : ICommand<DonationResponse>;

public sealed class RecordDonationCommandValidator : AbstractValidator<RecordDonationCommand>
{
    public RecordDonationCommandValidator()
    {
        RuleFor(x => x.TrustId).NotEmpty();

        RuleFor(x => x.DonorMemberId)
            .Must(id => id is null || Guid.TryParse(id, out _))
            .WithMessage("Choose a member, or leave blank for a donor with no account.");

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.DonorMemberId) || !string.IsNullOrWhiteSpace(x.DonorName))
            .WithMessage("Name the donor - a member, or at least a name.")
            .WithName("Donor");

        RuleFor(x => x.DonorName).MaximumLength(200);

        RuleFor(x => x.DonorPhone)
            .MaximumLength(20)
            .Matches(@"^\+?[0-9 ()-]{7,20}$")
            .When(x => !string.IsNullOrWhiteSpace(x.DonorPhone))
            .WithMessage("Digits, spaces, brackets and dashes, 7 to 20 of them.");

        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Purpose).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Mode).IsEnumName(typeof(DonationMode), caseSensitive: false);
    }
}

public sealed class RecordDonationCommandHandler(
    ITrustRepository trusts,
    IDonationRepository donations,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RecordDonationCommand, Result<DonationResponse>>
{
    public async Task<Result<DonationResponse>> Handle(
        RecordDonationCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<DonationResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var trust = await trusts.GetByIdAsync(command.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<DonationResponse>(
                Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        var donorMemberId = string.IsNullOrWhiteSpace(command.DonorMemberId)
            ? (Guid?)null
            : Guid.Parse(command.DonorMemberId);

        var mode = Enum.Parse<DonationMode>(command.Mode, ignoreCase: true);

        var donation = Donation.Record(
            trust.TenantId,
            trust.Id,
            donorMemberId,
            command.DonorName,
            command.DonorPhone,
            command.Amount,
            command.Purpose,
            mode,
            actorId,
            clock.UtcNow);

        donations.Add(donation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(donation.ToResponse());
    }
}
