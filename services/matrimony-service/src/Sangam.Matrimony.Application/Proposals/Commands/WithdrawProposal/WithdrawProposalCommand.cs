using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Proposals.Commands.WithdrawProposal;

/// <summary>The sender takes back a proposal nobody has decided yet - mirrors `WithdrawJoinRequestCommand`.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record WithdrawProposalCommand(Guid ProposalId) : ICommand<WithdrawProposalResult>;

public sealed record WithdrawProposalResult(bool Withdrawn);

public sealed class WithdrawProposalCommandHandler(
    IProposalRepository proposals,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<WithdrawProposalCommand, Result<WithdrawProposalResult>>
{
    public async Task<Result<WithdrawProposalResult>> Handle(WithdrawProposalCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<WithdrawProposalResult>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var proposal = await proposals.GetByIdAsync(command.ProposalId, cancellationToken);

        if (proposal is null || proposal.FromMemberId != memberId)
        {
            return Result.Failure<WithdrawProposalResult>(
                Error.NotFound("Proposal.NotFound", "No such proposal."));
        }

        if (tenantContext.TenantId is { } tenantId && proposal.TenantId != tenantId)
        {
            return Result.Failure<WithdrawProposalResult>(
                Error.NotFound("Proposal.NotFound", "No such proposal."));
        }

        if (proposal.Status != Domain.Proposals.ProposalStatus.Pending)
        {
            return Result.Failure<WithdrawProposalResult>(Error.Conflict(
                "Proposal.AlreadyDecided", "This proposal has already been decided."));
        }

        proposal.Withdraw(memberId, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new WithdrawProposalResult(true));
    }
}
