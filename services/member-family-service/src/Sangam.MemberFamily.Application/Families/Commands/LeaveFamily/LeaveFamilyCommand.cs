using FluentValidation;
using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;

namespace Sangam.MemberFamily.Application.Families.Commands.LeaveFamily;

/// <summary>
/// Leaves the household this member belongs to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Until this existed, joining a household was permanent.</b> An active
/// membership counts as belonging to one, so a member could not create their
/// own or ask another — and nothing could remove them. Marrying into a
/// different household, or a household splitting, had no path at all: erasing
/// your account was the only way out, which is a right being used as a
/// workaround for a missing feature.
/// </para>
/// <para>
/// It is a different act from <c>WithdrawJoinRequestCommand</c>, and the two are
/// deliberately not one call. Taking back a request nobody answered leaves no
/// trace and affects nobody; leaving a household you are in can move headship
/// and changes what other people see. Collapsing them would mean "cancel my
/// request" sometimes meant "leave my family".
/// </para>
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record LeaveFamilyCommand : ICommand<LeaveFamilyResult>;

/// <summary>
/// <paramref name="NewHeadMemberId"/> names whoever inherited the household when
/// the person leaving was its head, and is null otherwise.
/// </summary>
public sealed record LeaveFamilyResult(bool Left, Guid? NewHeadMemberId);

public sealed class LeaveFamilyCommandValidator : AbstractValidator<LeaveFamilyCommand>
{
    // No input to validate. Present so §4.3 reads as one validator per request
    // rather than "except where it did not seem to matter".
    public LeaveFamilyCommandValidator()
    {
    }
}

public sealed class LeaveFamilyCommandHandler(
    IFamilyRepository families,
    IChildRepository children,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser)
    : IRequestHandler<LeaveFamilyCommand, Result<LeaveFamilyResult>>
{
    public async Task<Result<LeaveFamilyResult>> Handle(
        LeaveFamilyCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<LeaveFamilyResult>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var family = await families.GetForMemberAsync(memberId, cancellationToken);
        var membership = family?.FindMember(memberId);

        if (family is null || membership is null)
        {
            // Not in one, so the end state asked for is the end state. Leaving
            // twice looks like leaving once.
            return Result.Success(new LeaveFamilyResult(false, null));
        }

        if (membership.Status == Domain.Families.FamilyMemberStatus.PendingJoinRequest)
        {
            // They have asked, not joined. There is a call for that, and it
            // says something different to whoever reads the audit log.
            return Result.Failure<LeaveFamilyResult>(Error.Conflict(
                "Family.NotAMemberYet",
                "You have asked to join that household and not been accepted. "
                + "Withdraw the request instead."));
        }

        var active = family.ActiveMembers();
        var householdChildren = await children.ListForFamilyAsync(family.Id, cancellationToken);

        // The one refusal, and it is about the children rather than about them.
        //
        // A child record exists on somebody's parental consent (DPDP s.9) and
        // lives in a household. If the last member walks out, those records stay
        // with nobody able to see or manage them - and nothing on this platform
        // can remove a child, so the state would be permanent. Refusing names
        // the gap instead of creating an orphan.
        if (active.Count == 1 && householdChildren.Count > 0)
        {
            return Result.Failure<LeaveFamilyResult>(Error.Conflict(
                "Family.WouldStrandChildren",
                "You are the only person left in this household and it has "
                + "children's records in it. Leaving would leave nobody able to "
                + "manage them, so it is refused."));
        }

        family.RemoveMember(memberId);

        var newHead = family.SucceedHeadAfterRemoval(memberId);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new LeaveFamilyResult(true, newHead));
    }
}
