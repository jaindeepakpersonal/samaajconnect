using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Members.Commands.RemoveTrustMember;

/// <summary>
/// Takes a member off a Trust's roster. Removing somebody already removed is
/// a no-op reported as success.
/// </summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record RemoveTrustMemberCommand(Guid TrustMemberId) : ICommand<TrustMemberResponse>;

public sealed class RemoveTrustMemberCommandValidator : AbstractValidator<RemoveTrustMemberCommand>
{
    public RemoveTrustMemberCommandValidator() => RuleFor(x => x.TrustMemberId).NotEmpty();
}

public sealed class RemoveTrustMemberCommandHandler(
    ITrustMemberRepository members,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<RemoveTrustMemberCommand, Result<TrustMemberResponse>>
{
    public async Task<Result<TrustMemberResponse>> Handle(
        RemoveTrustMemberCommand command,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Result.Failure<TrustMemberResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var member = await members.GetByIdAsync(command.TrustMemberId, cancellationToken);

        if (member is null)
        {
            return Result.Failure<TrustMemberResponse>(
                Error.NotFound("TrustMember.NotFound", "No such Trust member in this Samaaj."));
        }

        if (member.Remove(actorId, clock.UtcNow))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(member.ToResponse());
    }
}
