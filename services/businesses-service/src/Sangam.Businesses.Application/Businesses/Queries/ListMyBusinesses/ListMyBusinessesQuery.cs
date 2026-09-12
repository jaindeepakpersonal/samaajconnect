using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Businesses.Queries.ListMyBusinesses;

/// <summary>The caller's own businesses, active or not - for the management screen.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record ListMyBusinessesQuery : IQuery<IReadOnlyList<BusinessResponse>>;

public sealed class ListMyBusinessesQueryHandler(IBusinessRepository businesses, ICurrentUser currentUser)
    : IRequestHandler<ListMyBusinessesQuery, Result<IReadOnlyList<BusinessResponse>>>
{
    public async Task<Result<IReadOnlyList<BusinessResponse>>> Handle(
        ListMyBusinessesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<BusinessResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var owned = await businesses.ListOwnedAsync(memberId, cancellationToken);

        return Result.Success<IReadOnlyList<BusinessResponse>>(
            [.. owned.Select(b => b.ToResponse(onlyAvailableProducts: false))]);
    }
}
