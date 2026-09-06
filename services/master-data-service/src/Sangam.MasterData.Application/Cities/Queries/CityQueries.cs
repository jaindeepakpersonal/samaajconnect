using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Cities.Queries;

/// <summary>Cities, optionally narrowed to one country — this is a registration-form dropdown query.</summary>
[AllowAnonymousRequest]
public sealed record ListCitiesQuery(Guid? CountryId, bool IncludeInactive)
    : IQuery<IReadOnlyList<CityResponse>>;

public sealed class ListCitiesQueryHandler(ICityRepository cities)
    : IRequestHandler<ListCitiesQuery, Result<IReadOnlyList<CityResponse>>>
{
    public async Task<Result<IReadOnlyList<CityResponse>>> Handle(
        ListCitiesQuery query, CancellationToken cancellationToken)
    {
        var found = await cities.ListAsync(query.CountryId, query.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<CityResponse>>(
            [.. found.OrderBy(c => c.Name).Select(c => c.ToResponse())]);
    }
}

[AllowAnonymousRequest]
public sealed record GetCityByIdQuery(Guid Id) : IQuery<CityResponse>;

public sealed class GetCityByIdQueryHandler(ICityRepository cities)
    : IRequestHandler<GetCityByIdQuery, Result<CityResponse>>
{
    public async Task<Result<CityResponse>> Handle(GetCityByIdQuery query, CancellationToken cancellationToken)
    {
        var city = await cities.GetByIdAsync(query.Id, cancellationToken);

        return city is null
            ? Result.Failure<CityResponse>(Error.NotFound("City.NotFound", "No such city."))
            : Result.Success(city.ToResponse());
    }
}
