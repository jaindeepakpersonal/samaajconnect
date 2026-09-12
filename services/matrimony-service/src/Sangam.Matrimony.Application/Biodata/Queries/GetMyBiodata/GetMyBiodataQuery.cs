using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Biodata.Queries.GetMyBiodata;

/// <summary>The caller's own biodata, active or not - so a withdrawn member can still see/reactivate it.</summary>
[RequiresRoles(
    Roles.Member, Roles.FamilyHead, Roles.VolunteerGroupPresident,
    Roles.PathshalaTeacher, Roles.PathshalaStudent, Roles.ContentModerator,
    Roles.BoliManager, Roles.SamaajAdmin, Roles.SuperAdmin)]
public sealed record GetMyBiodataQuery : IQuery<MyBiodataResponse?>;

public sealed class GetMyBiodataQueryHandler(
    IBiodataRepository biodata, ICurrentUser currentUser, IDateTimeProvider clock)
    : IRequestHandler<GetMyBiodataQuery, Result<MyBiodataResponse?>>
{
    public async Task<Result<MyBiodataResponse?>> Handle(GetMyBiodataQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<MyBiodataResponse?>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var profile = await biodata.GetByIdAsync(memberId, cancellationToken);

        // Null, not a 404 - "you have not opted in yet" is a normal, expected
        // state for this query, not an error the screen needs to explain.
        return Result.Success(profile?.ToOwnerResponse(clock.UtcNow));
    }
}
