using MediatR;
using Sangam.Panch.Application.Abstractions;
using Sangam.Panch.Application.Common;
using Sangam.Panch.Application.Elections;
using Sangam.Panch.Application.Security;
using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Application.Panel;

/// <summary>The seated Samaaj Panch, and whether the asker sits on it.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetPanelQuery : IQuery<PanelResponse>;

public sealed class GetPanelQueryHandler(IPanelRepository panel, ICurrentUser currentUser)
    : IRequestHandler<GetPanelQuery, Result<PanelResponse>>
{
    public async Task<Result<PanelResponse>> Handle(GetPanelQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<PanelResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var members = await panel.ListAsync(cancellationToken);

        return Result.Success(new PanelResponse(
            [.. members.Select(m => new PanelMemberResponse(m.MemberId, m.ElectionId, m.SeatedAt))],
            PanchElection.SeatCount,
            members.Any(m => m.MemberId == memberId)));
    }
}
