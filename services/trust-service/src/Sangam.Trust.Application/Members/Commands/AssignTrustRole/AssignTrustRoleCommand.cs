using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Members.Commands.AssignTrustRole;

/// <summary>
/// Moves an active trustee into one of the Trust's roles by hand - the other
/// way a role is filled besides an election. Refused (409) when every seat is
/// taken, and for a removed trustee. Moving somebody into the role they hold is
/// success and changes nothing.
/// </summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record AssignTrustRoleCommand(Guid TrustMemberId, Guid RoleId) : ICommand<TrustMemberResponse>;

public sealed class AssignTrustRoleCommandValidator : AbstractValidator<AssignTrustRoleCommand>
{
    public AssignTrustRoleCommandValidator()
    {
        RuleFor(x => x.TrustMemberId).NotEmpty();
        RuleFor(x => x.RoleId).NotEmpty();
    }
}

public sealed class AssignTrustRoleCommandHandler(
    ITrustRepository trusts,
    ITrustMemberRepository members,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<AssignTrustRoleCommand, Result<TrustMemberResponse>>
{
    public async Task<Result<TrustMemberResponse>> Handle(
        AssignTrustRoleCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<TrustMemberResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var member = await members.GetByIdAsync(command.TrustMemberId, cancellationToken);
        var trust = member is null ? null : await trusts.GetByIdAsync(member.TrustId, cancellationToken);

        if (member is null || trust is null)
        {
            return Result.Failure<TrustMemberResponse>(
                Error.NotFound("TrustMember.NotFound", "No such Trust member in this Samaaj."));
        }

        if (member.Status != TrustMemberStatus.Active)
        {
            return Result.Failure<TrustMemberResponse>(Error.Conflict(
                "TrustMember.Removed", "This trustee has been removed from the roster. Add them back first."));
        }

        var role = trust.FindRole(command.RoleId);

        if (role is null)
        {
            return Result.Failure<TrustMemberResponse>(
                Error.NotFound("TrustRole.NotFound", "No such role on this Trust."));
        }

        if (member.RoleId != role.Id
            && role.Seats is { } seats
            && await members.CountActiveHoldersAsync(trust.Id, role.Id, cancellationToken) >= seats)
        {
            return Result.Failure<TrustMemberResponse>(Error.Conflict(
                "TrustRole.Full", "Every seat of this role is taken."));
        }

        member.AssignRole(role.Id, actorId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(member.ToResponse(trust));
    }
}
