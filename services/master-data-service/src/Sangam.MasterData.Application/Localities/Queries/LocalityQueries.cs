using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Localities.Queries;

[AllowAnonymousRequest]
public sealed record ListLocalitiesQuery(Guid? CityId, bool IncludeInactive)
    : IQuery<IReadOnlyList<LocalityResponse>>;

public sealed class ListLocalitiesQueryHandler(ILocalityRepository localities)
    : IRequestHandler<ListLocalitiesQuery, Result<IReadOnlyList<LocalityResponse>>>
{
    public async Task<Result<IReadOnlyList<LocalityResponse>>> Handle(
        ListLocalitiesQuery query, CancellationToken cancellationToken)
    {
        var found = await localities.ListAsync(query.CityId, query.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<LocalityResponse>>(
            [.. found.OrderBy(l => l.Name).Select(l => l.ToResponse())]);
    }
}

[AllowAnonymousRequest]
public sealed record GetLocalityByIdQuery(Guid Id) : IQuery<LocalityResponse>;

public sealed class GetLocalityByIdQueryHandler(ILocalityRepository localities)
    : IRequestHandler<GetLocalityByIdQuery, Result<LocalityResponse>>
{
    public async Task<Result<LocalityResponse>> Handle(
        GetLocalityByIdQuery query, CancellationToken cancellationToken)
    {
        var locality = await localities.GetByIdAsync(query.Id, cancellationToken);

        return locality is null
            ? Result.Failure<LocalityResponse>(Error.NotFound("Locality.NotFound", "No such locality."))
            : Result.Success(locality.ToResponse());
    }
}
