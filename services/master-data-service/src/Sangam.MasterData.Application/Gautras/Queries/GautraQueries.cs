using MediatR;
using Sangam.MasterData.Application.Abstractions;
using Sangam.MasterData.Application.Common;
using Sangam.MasterData.Application.Security;

namespace Sangam.MasterData.Application.Gautras.Queries;

/// <summary>
/// The calling Samaaj's own Gautra list. Anonymous on purpose: a member
/// picks their Gautra during registration, after choosing a Samaaj from the
/// directory but before they hold a token - the tenant comes from the
/// gateway's X-Tenant-Id header alone in that case, exactly like the rest of
/// registration (root CLAUDE.md §6).
/// </summary>
[AllowAnonymousRequest]
public sealed record ListGautrasQuery(bool IncludeInactive) : IQuery<IReadOnlyList<GautraResponse>>;

public sealed class ListGautrasQueryHandler(IGautraRepository gautras)
    : IRequestHandler<ListGautrasQuery, Result<IReadOnlyList<GautraResponse>>>
{
    public async Task<Result<IReadOnlyList<GautraResponse>>> Handle(
        ListGautrasQuery query, CancellationToken cancellationToken)
    {
        var found = await gautras.ListAsync(query.IncludeInactive, cancellationToken);

        return Result.Success<IReadOnlyList<GautraResponse>>(
            [.. found.OrderBy(g => g.Name).Select(g => g.ToResponse())]);
    }
}

[AllowAnonymousRequest]
public sealed record GetGautraByIdQuery(Guid Id) : IQuery<GautraResponse>;

public sealed class GetGautraByIdQueryHandler(IGautraRepository gautras)
    : IRequestHandler<GetGautraByIdQuery, Result<GautraResponse>>
{
    public async Task<Result<GautraResponse>> Handle(
        GetGautraByIdQuery query, CancellationToken cancellationToken)
    {
        var gautra = await gautras.GetByIdAsync(query.Id, cancellationToken);

        return gautra is null
            ? Result.Failure<GautraResponse>(Error.NotFound("Gautra.NotFound", "No such Gautra in this Samaaj."))
            : Result.Success(gautra.ToResponse());
    }
}
