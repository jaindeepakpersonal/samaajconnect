using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Biodata;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Proposals.Commands.RespondToProposal;

/// <summary>The recipient accepts or declines. Refused as not-found to anyone else - whether a proposal is pending is the recipient's own business.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record RespondToProposalCommand(Guid ProposalId, bool Accept) : ICommand<ProposalResponse>;

public sealed class RespondToProposalCommandHandler(
    IProposalRepository proposals,
    IBiodataRepository biodata,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<RespondToProposalCommand, Result<ProposalResponse>>
{
    public async Task<Result<ProposalResponse>> Handle(RespondToProposalCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<ProposalResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var proposal = await proposals.GetByIdAsync(command.ProposalId, cancellationToken);

        if (proposal is null || proposal.ToMemberId != memberId)
        {
            return Result.Failure<ProposalResponse>(
                Error.NotFound("Proposal.NotFound", "No such proposal."));
        }

        if (tenantContext.TenantId is { } tenantId && proposal.TenantId != tenantId)
        {
            return Result.Failure<ProposalResponse>(
                Error.NotFound("Proposal.NotFound", "No such proposal."));
        }

        if (proposal.Status != Domain.Proposals.ProposalStatus.Pending)
        {
            return Result.Failure<ProposalResponse>(Error.Conflict(
                "Proposal.AlreadyDecided", "This proposal has already been decided."));
        }

        var now = clock.UtcNow;

        proposal.Respond(command.Accept, memberId, now);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var otherParty = await biodata.GetByIdAsync(proposal.FromMemberId, cancellationToken);

        return Result.Success(new ProposalResponse(
            proposal.Id, proposal.FromMemberId, proposal.ToMemberId, proposal.Status.ToString(),
            proposal.Message, proposal.SentAt, proposal.RespondedAt, otherParty?.ToResponse(now)));
    }
}
