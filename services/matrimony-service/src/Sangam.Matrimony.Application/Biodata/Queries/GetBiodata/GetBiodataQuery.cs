using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Biodata.Queries.GetBiodata;

/// <summary>One candidate's full biodata, by member id. 404 if inactive, erased, or not in the caller's Samaaj.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetBiodataQuery(Guid MemberId) : IQuery<BiodataResponse>;

public sealed class GetBiodataQueryHandler(
    IBiodataRepository biodata, IEligibilityRepository eligibility, IDateTimeProvider clock)
    : IRequestHandler<GetBiodataQuery, Result<BiodataResponse>>
{
    public async Task<Result<BiodataResponse>> Handle(GetBiodataQuery query, CancellationToken cancellationToken)
    {
        var profile = await biodata.GetByIdAsync(query.MemberId, cancellationToken);

        // A member who is not, or no longer, eligible is not on offer - the same
        // "no such biodata" as a withdrawn one, so the refusal says nothing about why.
        if (profile is null || !profile.IsActive
            || !await MatrimonyEligibilityRules.IsEligibleAsync(eligibility, profile.Id, cancellationToken))
        {
            return Result.Failure<BiodataResponse>(
                Error.NotFound("Biodata.NotFound", "No such biodata in this Samaaj."));
        }

        return Result.Success(profile.ToResponse(clock.UtcNow));
    }
}
