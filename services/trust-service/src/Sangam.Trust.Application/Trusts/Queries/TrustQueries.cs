using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;

namespace Sangam.Trust.Application.Trusts.Queries;

/// <summary>This Samaaj's Trusts, optionally narrowed to one locality, newest first.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListTrustsQuery(Guid? LocalityId) : IQuery<IReadOnlyList<TrustResponse>>;

public sealed class ListTrustsQueryHandler(ITrustRepository trusts)
    : IRequestHandler<ListTrustsQuery, Result<IReadOnlyList<TrustResponse>>>
{
    public async Task<Result<IReadOnlyList<TrustResponse>>> Handle(
        ListTrustsQuery query,
        CancellationToken cancellationToken)
    {
        var found = await trusts.ListAsync(query.LocalityId, cancellationToken);

        return Result.Success<IReadOnlyList<TrustResponse>>([.. found.Select(t => t.ToResponse())]);
    }
}

/// <summary>One Trust.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record GetTrustQuery(Guid TrustId) : IQuery<TrustResponse>;

public sealed class GetTrustQueryHandler(ITrustRepository trusts)
    : IRequestHandler<GetTrustQuery, Result<TrustResponse>>
{
    public async Task<Result<TrustResponse>> Handle(GetTrustQuery query, CancellationToken cancellationToken)
    {
        var trust = await trusts.GetByIdAsync(query.TrustId, cancellationToken);

        return trust is null
            ? Result.Failure<TrustResponse>(Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."))
            : Result.Success(trust.ToResponse());
    }
}
