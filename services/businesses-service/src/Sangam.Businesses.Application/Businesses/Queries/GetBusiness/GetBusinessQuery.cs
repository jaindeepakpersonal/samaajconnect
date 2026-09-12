using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Businesses.Queries.GetBusiness;

/// <summary>
/// One business's public detail. 404 if inactive and the caller is not its
/// owner, or if it belongs to a different Samaaj. The owner sees every
/// product, including unavailable ones; anyone else sees only what is
/// currently for sale.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetBusinessQuery(Guid BusinessId) : IQuery<BusinessResponse>;

public sealed class GetBusinessQueryHandler(IBusinessRepository businesses, ICurrentUser currentUser)
    : IRequestHandler<GetBusinessQuery, Result<BusinessResponse>>
{
    public async Task<Result<BusinessResponse>> Handle(GetBusinessQuery query, CancellationToken cancellationToken)
    {
        var business = await businesses.GetByIdAsync(query.BusinessId, cancellationToken);

        if (business is null)
        {
            return Result.Failure<BusinessResponse>(Error.NotFound("Business.NotFound", "No such business in this Samaaj."));
        }

        var isOwner = currentUser.UserId == business.OwnerMemberId;

        if (!business.IsActive && !isOwner)
        {
            return Result.Failure<BusinessResponse>(Error.NotFound("Business.NotFound", "No such business in this Samaaj."));
        }

        return Result.Success(business.ToResponse(onlyAvailableProducts: !isOwner));
    }
}
