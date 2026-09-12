using MediatR;
using Sangam.Matrimony.Application.Abstractions;
using Sangam.Matrimony.Application.Common;
using Sangam.Matrimony.Application.Security;

namespace Sangam.Matrimony.Application.Biodata.Queries.GetBiodata;

/// <summary>One candidate's full biodata, by member id. 404 if inactive, erased, or not in the caller's Samaaj.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetBiodataQuery(Guid MemberId) : IQuery<BiodataResponse>;

public sealed class GetBiodataQueryHandler(IBiodataRepository biodata, IDateTimeProvider clock)
    : IRequestHandler<GetBiodataQuery, Result<BiodataResponse>>
{
    public async Task<Result<BiodataResponse>> Handle(GetBiodataQuery query, CancellationToken cancellationToken)
    {
        var profile = await biodata.GetByIdAsync(query.MemberId, cancellationToken);

        if (profile is null || !profile.IsActive)
        {
            return Result.Failure<BiodataResponse>(
                Error.NotFound("Biodata.NotFound", "No such biodata in this Samaaj."));
        }

        return Result.Success(profile.ToResponse(clock.UtcNow));
    }
}
