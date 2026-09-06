using FluentValidation;
using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;
using Sangam.MasterData.Domain.Lookups;

namespace Sangam.MasterData.Application.LookupItems.Queries;

/// <summary>
/// One category's list, active items first by sort order — this is exactly
/// the shape a startup cache warmup (or a dropdown) wants.
/// </summary>
[AllowAnonymousRequest]
public sealed record ListLookupItemsQuery(string Category, bool IncludeInactive)
    : IQuery<IReadOnlyList<LookupItemResponse>>;

public sealed class ListLookupItemsQueryValidator : AbstractValidator<ListLookupItemsQuery>
{
    public ListLookupItemsQueryValidator()
    {
        RuleFor(x => x.Category)
            .NotEmpty()
            .Must(c => Enum.TryParse<LookupCategory>(c, ignoreCase: true, out _))
            .WithMessage("Category must be one of Country, ProfessionType, Gender, MaritalStatus.");
    }
}

public sealed class ListLookupItemsQueryHandler(ILookupItemRepository lookupItems)
    : IRequestHandler<ListLookupItemsQuery, Result<IReadOnlyList<LookupItemResponse>>>
{
    public async Task<Result<IReadOnlyList<LookupItemResponse>>> Handle(
        ListLookupItemsQuery query, CancellationToken cancellationToken)
    {
        var category = Enum.Parse<LookupCategory>(query.Category, ignoreCase: true);

        var found = await lookupItems.ListAsync(category, query.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<LookupItemResponse>>(
            [.. found.OrderBy(i => i.SortOrder).Select(i => i.ToResponse())]);
    }
}

[AllowAnonymousRequest]
public sealed record GetLookupItemByIdQuery(Guid Id) : IQuery<LookupItemResponse>;

public sealed class GetLookupItemByIdQueryHandler(ILookupItemRepository lookupItems)
    : IRequestHandler<GetLookupItemByIdQuery, Result<LookupItemResponse>>
{
    public async Task<Result<LookupItemResponse>> Handle(
        GetLookupItemByIdQuery query, CancellationToken cancellationToken)
    {
        var item = await lookupItems.GetByIdAsync(query.Id, cancellationToken);

        return item is null
            ? Result.Failure<LookupItemResponse>(Error.NotFound("LookupItem.NotFound", "No such lookup item."))
            : Result.Success(item.ToResponse());
    }
}
