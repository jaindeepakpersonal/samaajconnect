using FluentValidation;
using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Members.Commands.UpdateTrustMember;

/// <summary>Changes an active trustee's position title.</summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record UpdateTrustMemberCommand(Guid TrustMemberId, string? PositionTitle)
    : ICommand<TrustMemberResponse>;

public sealed class UpdateTrustMemberCommandValidator : AbstractValidator<UpdateTrustMemberCommand>
{
    public UpdateTrustMemberCommandValidator()
    {
        RuleFor(x => x.TrustMemberId).NotEmpty();
        RuleFor(x => x.PositionTitle).MaximumLength(200);
    }
}

public sealed class UpdateTrustMemberCommandHandler(
    ITrustMemberRepository members,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<UpdateTrustMemberCommand, Result<TrustMemberResponse>>
{
    public async Task<Result<TrustMemberResponse>> Handle(
        UpdateTrustMemberCommand command,
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

        if (!member.UpdatePosition(command.PositionTitle, actorId, clock.UtcNow))
        {
            return Result.Failure<TrustMemberResponse>(Error.Conflict(
                "TrustMember.Removed", "This member is no longer on the Trust's roster."));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(member.ToResponse());
    }
}
