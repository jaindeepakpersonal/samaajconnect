using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;

namespace Sangam.MemberFamily.Application.Families.Queries.GetMyFamily;

public sealed class GetMyFamilyQueryHandler(
    IFamilyRepository families,
    IMemberProfileRepository profiles,
    ICurrentUser currentUser)
    : IRequestHandler<GetMyFamilyQuery, Result<FamilyResponse>>
{
    public async Task<Result<FamilyResponse>> Handle(
        GetMyFamilyQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<FamilyResponse>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        if (family is null)
        {
            return Result.Failure<FamilyResponse>(Error.NotFound(
                "Family.None", "You do not belong to a family yet."));
        }

        var memberProfiles = await profiles.SearchAsync(null, null, 200, cancellationToken);

        return Result.Success(family.ToResponse(memberId, memberProfiles));
    }
}
