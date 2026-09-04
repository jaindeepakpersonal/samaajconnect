using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Families;

namespace Sangam.MemberFamily.Application.Families.Commands.WithdrawJoinRequest;

/// <summary>
/// Takes back a request to join a household that nobody has decided.
/// </summary>
/// <remarks>
/// <para>
/// <b>The member's own way out, and until now there was none.</b> A pending
/// request counts as belonging to a household - deliberately, so nobody can ask
/// two families at once and have both heads accept - and nothing could cancel
/// one. A head who was slow, or who never looked, or who erased their account,
/// left that member permanently unable to join anywhere or create a household
/// of their own. The only way out ran through somebody else.
/// </para>
/// <para>
/// It takes no parameters. A member has at most one standing request by
/// construction, so naming which one to withdraw would be asking for a fact the
/// caller cannot get wrong and the server already knows - and an id parameter
/// would be a thing to check rather than a thing to trust.
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record WithdrawJoinRequestCommand : ICommand<WithdrawJoinRequestResult>;

/// <summary>
/// <paramref name="Withdrawn"/> is false when there was nothing pending, which
/// is success: the end state the caller asked for is the end state.
/// </summary>
public sealed record WithdrawJoinRequestResult(bool Withdrawn);

public sealed class WithdrawJoinRequestCommandValidator
    : AbstractValidator<WithdrawJoinRequestCommand>
{
    // Nothing to validate: the command carries no input. Present so that
    // §4.3 reads as "one validator per request" rather than "except where we
    // decided it did not matter" - and so a parameter added later lands
    // somewhere that already exists.
    public WithdrawJoinRequestCommandValidator()
    {
    }
}

public sealed class WithdrawJoinRequestCommandHandler(
    IFamilyRepository families,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser)
    : IRequestHandler<WithdrawJoinRequestCommand, Result<WithdrawJoinRequestResult>>
{
    public async Task<Result<WithdrawJoinRequestResult>> Handle(
        WithdrawJoinRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<WithdrawJoinRequestResult>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        if (family is null)
        {
            // No household and no request. Withdrawing twice looks exactly like
            // withdrawing once, which is what a member clicking twice deserves.
            return Result.Success(new WithdrawJoinRequestResult(false));
        }

        var outcome = family.WithdrawJoinRequest(memberId);

        return outcome switch
        {
            Family.WithdrawOutcome.Withdrawn => await SaveAsync(cancellationToken),

            Family.WithdrawOutcome.NothingPending =>
                Result.Success(new WithdrawJoinRequestResult(false)),

            // Refused by name rather than silently doing nothing. The head
            // accepted while the member was deciding to withdraw, so they are
            // in the household now - and a call that quietly succeeded would
            // leave them believing they had cancelled something they had not.
            _ => Result.Failure<WithdrawJoinRequestResult>(Error.Conflict(
                "Family.AlreadyAccepted",
                "Your request was accepted, so you are in that household now. "
                + "Ask the family head if you want to leave.")),
        };
    }

    private async Task<Result<WithdrawJoinRequestResult>> SaveAsync(CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new WithdrawJoinRequestResult(true));
    }
}
