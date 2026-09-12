using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Biodata;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Proposals.Queries.ListReceivedProposals;

[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record ListReceivedProposalsQuery : IQuery<IReadOnlyList<ProposalResponse>>;

public sealed class ListReceivedProposalsQueryHandler(
    IProposalRepository proposals, IBiodataRepository biodata, ICurrentUser currentUser, IDateTimeProvider clock)
    : IRequestHandler<ListReceivedProposalsQuery, Result<IReadOnlyList<ProposalResponse>>>
{
    public async Task<Result<IReadOnlyList<ProposalResponse>>> Handle(
        ListReceivedProposalsQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<ProposalResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var received = await proposals.ListReceivedAsync(memberId, cancellationToken);
        var now = clock.UtcNow;
        var responses = new List<ProposalResponse>(received.Count);

        foreach (var proposal in received)
        {
            var otherParty = await biodata.GetByIdAsync(proposal.FromMemberId, cancellationToken);

            responses.Add(new ProposalResponse(
                proposal.Id, proposal.FromMemberId, proposal.ToMemberId, proposal.Status.ToString(),
                proposal.Message, proposal.SentAt, proposal.RespondedAt, otherParty?.ToResponse(now)));
        }

        return Result.Success<IReadOnlyList<ProposalResponse>>(responses);
    }
}
