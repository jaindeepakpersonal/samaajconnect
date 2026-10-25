using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Biodata.Queries.GetMyEligibility;

/// <summary>
/// Whether the caller may keep a biodata: Eligible, NotEligible, or Unknown when
/// they have not set their marital status - so the page can say which, and point
/// at My Profile, rather than offering a form the service will refuse.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetMyMatrimonyEligibilityQuery : IQuery<MatrimonyEligibilityResponse>;

public sealed record MatrimonyEligibilityResponse(string Status);

public sealed class GetMyMatrimonyEligibilityQueryHandler(IEligibilityRepository eligibility, ICurrentUser currentUser)
    : IRequestHandler<GetMyMatrimonyEligibilityQuery, Result<MatrimonyEligibilityResponse>>
{
    public async Task<Result<MatrimonyEligibilityResponse>> Handle(
        GetMyMatrimonyEligibilityQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<MatrimonyEligibilityResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var row = await eligibility.GetAsync(memberId, cancellationToken);

        return Result.Success(new MatrimonyEligibilityResponse(
            row is null ? "Unknown" : row.IsEligible ? "Eligible" : "NotEligible"));
    }
}
