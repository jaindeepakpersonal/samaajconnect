using FluentValidation;
using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Biodata;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Proposals.Commands.SendProposal;

[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record SendProposalCommand(Guid ToMemberId, string? Message) : ICommand<ProposalResponse>;

public sealed class SendProposalCommandValidator : AbstractValidator<SendProposalCommand>
{
    public SendProposalCommandValidator()
    {
        RuleFor(x => x.ToMemberId).NotEmpty();
        RuleFor(x => x.Message).MaximumLength(500);
    }
}

public sealed class SendProposalCommandHandler(
    IProposalRepository proposals,
    IBiodataRepository biodata,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    ITenantContext tenantContext,
    IDateTimeProvider clock)
    : IRequestHandler<SendProposalCommand, Result<ProposalResponse>>
{
    public async Task<Result<ProposalResponse>> Handle(SendProposalCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<ProposalResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        if (memberId == command.ToMemberId)
        {
            return Result.Failure<ProposalResponse>(
                Error.Validation(new Dictionary<string, string[]>
                {
                    [nameof(command.ToMemberId)] = ["You cannot send a proposal to yourself."],
                }));
        }

        var candidate = await biodata.GetByIdAsync(command.ToMemberId, cancellationToken);

        if (candidate is null || !candidate.IsActive)
        {
            return Result.Failure<ProposalResponse>(
                Error.NotFound("Biodata.NotFound", "No such biodata in this Samaaj."));
        }

        if (await proposals.HasActiveBetweenAsync(memberId, command.ToMemberId, cancellationToken))
        {
            return Result.Failure<ProposalResponse>(Error.Conflict(
                "Proposal.AlreadyActive",
                "There is already a pending or accepted proposal between you and this member."));
        }

        if (tenantContext.TenantId is not { } tenantId || tenantId == Guid.Empty)
        {
            return Result.Failure<ProposalResponse>(Error.Forbidden(
                "Proposal.NoSamaaj", "Select a Samaaj before sending a proposal."));
        }

        var now = clock.UtcNow;

        var proposal = Domain.Proposals.Proposal.Send(tenantId, memberId, command.ToMemberId, command.Message, now);

        proposals.Add(proposal);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new ProposalResponse(
            proposal.Id, proposal.FromMemberId, proposal.ToMemberId, proposal.Status.ToString(),
            proposal.Message, proposal.SentAt, proposal.RespondedAt, candidate.ToResponse(now)));
    }
}
