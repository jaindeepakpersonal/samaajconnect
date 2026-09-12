using FluentValidation;
using MediatR;
using Sangam.Businesses.Application.Abstractions;
using Sangam.Businesses.Application.Common;
using Sangam.Businesses.Application.Security;

namespace Sangam.Businesses.Application.Businesses.Queries.SearchBusinesses;

/// <summary>
/// The directory: active businesses in the caller's own Samaaj, filtered by
/// category and/or location. Capped at 50 by default, matching
/// `SearchMembersQuery`'s own default limit and Matrimony's
/// `SearchBiodataQuery`'s own cap - this platform's scale does not need more.
/// </summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record SearchBusinessesQuery(
    Guid? CategoryId,
    Guid? CityId,
    Guid? LocalityId,
    string? Term,
    int Limit = 50) : IQuery<IReadOnlyList<BusinessResponse>>;

public sealed class SearchBusinessesQueryValidator : AbstractValidator<SearchBusinessesQuery>
{
    public SearchBusinessesQueryValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.Term).MaximumLength(120);
    }
}

public sealed class SearchBusinessesQueryHandler(IBusinessRepository businesses)
    : IRequestHandler<SearchBusinessesQuery, Result<IReadOnlyList<BusinessResponse>>>
{
    public async Task<Result<IReadOnlyList<BusinessResponse>>> Handle(
        SearchBusinessesQuery query, CancellationToken cancellationToken)
    {
        var filters = new BusinessSearchFilters(
            query.CategoryId, query.CityId, query.LocalityId, query.Term, query.Limit);

        var results = await businesses.SearchAsync(filters, cancellationToken);

        return Result.Success<IReadOnlyList<BusinessResponse>>(
            [.. results.Select(b => b.ToResponse(onlyAvailableProducts: true))]);
    }
}
