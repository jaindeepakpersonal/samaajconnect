using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.States.Queries;

/// <summary>States, optionally narrowed to one country — this is a registration-form dropdown query.</summary>
[AllowAnonymousRequest]
public sealed record ListStatesQuery(Guid? CountryId, bool IncludeInactive)
    : IQuery<IReadOnlyList<StateResponse>>;

public sealed class ListStatesQueryHandler(IStateRepository states)
    : IRequestHandler<ListStatesQuery, Result<IReadOnlyList<StateResponse>>>
{
    public async Task<Result<IReadOnlyList<StateResponse>>> Handle(
        ListStatesQuery query, CancellationToken cancellationToken)
    {
        var found = await states.ListAsync(query.CountryId, query.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<StateResponse>>(
            [.. found.OrderBy(s => s.Name).Select(s => s.ToResponse())]);
    }
}

[AllowAnonymousRequest]
public sealed record GetStateByIdQuery(Guid Id) : IQuery<StateResponse>;

public sealed class GetStateByIdQueryHandler(IStateRepository states)
    : IRequestHandler<GetStateByIdQuery, Result<StateResponse>>
{
    public async Task<Result<StateResponse>> Handle(GetStateByIdQuery query, CancellationToken cancellationToken)
    {
        var state = await states.GetByIdAsync(query.Id, cancellationToken);

        return state is null
            ? Result.Failure<StateResponse>(Error.NotFound("State.NotFound", "No such state."))
            : Result.Success(state.ToResponse());
    }
}
