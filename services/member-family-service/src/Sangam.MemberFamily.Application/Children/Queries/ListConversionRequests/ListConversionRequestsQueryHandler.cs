using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;

namespace Sangam.MemberFamily.Application.Children.Queries.ListConversionRequests;

public sealed class ListConversionRequestsQueryHandler(
    IChildConversionRepository conversions,
    IChildRepository children)
    : IRequestHandler<ListConversionRequestsQuery, Result<IReadOnlyList<ConversionRequestResponse>>>
{
    public async Task<Result<IReadOnlyList<ConversionRequestResponse>>> Handle(
        ListConversionRequestsQuery query,
        CancellationToken cancellationToken)
    {
        var pending = await conversions.ListPendingAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return Result.Success<IReadOnlyList<ConversionRequestResponse>>([]);
        }

        // Both sides are tenant-filtered, so this names only children in the
        // admin's own Samaaj.
        var names = (await children.ListAllAsync(cancellationToken))
            .ToDictionary(child => child.Id, child => child.FullName);

        IReadOnlyList<ConversionRequestResponse> results = pending
            .Select(request => request.ToResponse(
                names.GetValueOrDefault(request.ChildProfileId, "Unknown child")))
            .ToList();

        return Result.Success(results);
    }
}
