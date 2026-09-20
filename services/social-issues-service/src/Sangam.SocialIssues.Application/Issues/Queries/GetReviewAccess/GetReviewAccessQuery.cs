using MediatR;
using Sangam.SocialIssues.Application.Common;
using Sangam.SocialIssues.Application.Security;

namespace Sangam.SocialIssues.Application.Issues.Queries.GetReviewAccess;

/// <summary>
/// Whether the asker reviews issues, so a portal never asks for a queue it may
/// not have and can say why a screen is read-only.
/// </summary>
/// <remarks>
/// <c>CanReview</c> is <i>can act</i>, not <i>can see</i>: a seated Panch member,
/// or an administrator while no Panch is seated. An administrator once a Panch
/// exists gets <c>CanSeeQueue</c> and no <c>CanReview</c>.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetReviewAccessQuery : IQuery<ReviewAccessResponse>;

public sealed record ReviewAccessResponse(
    bool CanReview, bool CanSeeQueue, bool PanelSeated, bool IsPanelMember);

public sealed class GetReviewAccessQueryHandler(ReviewAccessResolver reviewAccess)
    : IRequestHandler<GetReviewAccessQuery, Result<ReviewAccessResponse>>
{
    public async Task<Result<ReviewAccessResponse>> Handle(
        GetReviewAccessQuery query,
        CancellationToken cancellationToken)
    {
        var access = await reviewAccess.ResolveAsync(cancellationToken);

        return Result.Success(new ReviewAccessResponse(
            access.CanAct, access.CanSeeQueue, access.PanelSeated, access.IsPanelMember));
    }
}
