using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Media;

namespace Sangam.MemberFamily.Application.Children.Commands.WithdrawParentalConsent;

/// <summary>
/// A parent withdrawing the consent one child's record is held on (DPDP s.6(4)).
/// </summary>
/// <remarks>
/// <para>
/// <b>Until this existed the right was unreachable.</b> A child's record exists
/// because a parent consented (s.9), and this service said so in three places -
/// and the only way to withdraw that consent was
/// <c>POST /v1/identity/me/erase</c>: destroy your own account, your household
/// membership, and everything you have ever written on the platform. Section
/// 6(4) requires withdrawing to be as easy as giving, and giving was one tick
/// beside a notice. Making the right conditional on surrendering unrelated ones
/// is not comparable ease.
/// </para>
/// <para>
/// <b>The consent-giver, and nobody else.</b> Not the current head of the
/// household, not a Samaaj administrator, not another parent in it. The consent
/// is that person's and s.6(4) is their right; a head who inherited the
/// household did not give it and cannot take it back. The same reasoning that
/// made erasure follow <c>ListByConsentGiverAsync</c> rather than the family
/// tree applies here, one step earlier.
/// </para>
/// <para>
/// <b>A converted child is refused.</b> Once conversion completed, that person
/// holds their own account and their own consent; the record is no longer held
/// on a parent's. Allowing it then would let a parent erase an adult's data on
/// their own say-so - and that adult already has s.12 for themselves.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record WithdrawParentalConsentCommand(Guid ChildId)
    : ICommand<WithdrawParentalConsentResult>;

/// <summary>
/// <paramref name="Withdrawn"/> is false only when there was nothing left to
/// withdraw, which is success rather than an error.
/// </summary>
public sealed record WithdrawParentalConsentResult(bool Withdrawn);

public sealed class WithdrawParentalConsentCommandValidator
    : AbstractValidator<WithdrawParentalConsentCommand>
{
    public WithdrawParentalConsentCommandValidator()
    {
        RuleFor(x => x.ChildId).NotEmpty();
    }
}

public sealed class WithdrawParentalConsentCommandHandler(
    IChildRepository children,
    IImageStore images,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<WithdrawParentalConsentCommand, Result<WithdrawParentalConsentResult>>
{
    public async Task<Result<WithdrawParentalConsentResult>> Handle(
        WithdrawParentalConsentCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<WithdrawParentalConsentResult>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var child = await children.GetByIdAsync(command.ChildId, cancellationToken);

        if (child is null)
        {
            return Result.Failure<WithdrawParentalConsentResult>(
                Error.NotFound("Child.NotFound", "No such child record."));
        }

        // The IDOR guard root CLAUDE.md §6 requires on every write path.
        if (tenantContext.TenantId is { } tenantId && child.TenantId != tenantId)
        {
            return Result.Failure<WithdrawParentalConsentResult>(
                Error.NotFound("Child.NotFound", "No such child record."));
        }

        // Not found rather than forbidden: whether a particular child record
        // exists is not something a caller who has no claim on it should be
        // able to confirm by the shape of the refusal.
        if (child.ParentalConsent?.GivenByMemberId != memberId)
        {
            return Result.Failure<WithdrawParentalConsentResult>(
                Error.NotFound("Child.NotFound", "No such child record."));
        }

        if (child.Status == ChildStatus.Converted)
        {
            return Result.Failure<WithdrawParentalConsentResult>(Error.Conflict(
                "Child.AlreadyConverted",
                "This person has their own account now, and their data is held on their "
                + "own consent rather than yours. They can remove it themselves from "
                + "their privacy screen."));
        }

        if (child.ParentalConsent is { Stands: false })
        {
            // Already withdrawn. Saying so as success rather than as an error is
            // the same rule the rest of this service follows: the end state
            // asked for is the end state.
            return Result.Success(new WithdrawParentalConsentResult(false));
        }

        child.WithdrawParentalConsent(memberId, clock.UtcNow);

        // The bytes, not only the reference. A photograph of a child is the last
        // thing that should survive the consent it was held under, and a row of
        // bytes nothing points at is unreachable rather than removed.
        await images.RemoveAllForOwnerAsync(
            child.TenantId, ImageOwnerKind.Child, child.Id, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new WithdrawParentalConsentResult(true));
    }
}
