using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Collections;

namespace Sangam.Trust.Application.Collections.Queries;

/// <summary>One Trust's collection ledger - what's due and what's settled, newest first.</summary>
[RequiresPermission(PermissionKeys.TrustManage)]
public sealed record ListCollectionsQuery(Guid TrustId, CollectionStatus? Status)
    : IQuery<IReadOnlyList<CollectionResponse>>;

public sealed class ListCollectionsQueryHandler(ICollectionRepository collections)
    : IRequestHandler<ListCollectionsQuery, Result<IReadOnlyList<CollectionResponse>>>
{
    public async Task<Result<IReadOnlyList<CollectionResponse>>> Handle(
        ListCollectionsQuery query, CancellationToken cancellationToken)
    {
        var found = await collections.ListByTrustAsync(query.TrustId, query.Status, cancellationToken);

        return Result.Success<IReadOnlyList<CollectionResponse>>([.. found.Select(c => c.ToResponse())]);
    }
}

/// <summary>
/// What the caller owes, to any Trust in their Samaaj - a Boli they won, a
/// pledge they made - still due, oldest first.
/// </summary>
/// <remarks>
/// <c>Members.Read</c> and nothing else, because the answer is only ever the
/// caller's own rows: the payer is the signed-in member, never a parameter,
/// so there is nobody else's debt this can be pointed at.
/// </remarks>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListMyDuesQuery : IQuery<IReadOnlyList<CollectionResponse>>;

public sealed class ListMyDuesQueryHandler(ICollectionRepository collections, ICurrentUser currentUser)
    : IRequestHandler<ListMyDuesQuery, Result<IReadOnlyList<CollectionResponse>>>
{
    public async Task<Result<IReadOnlyList<CollectionResponse>>> Handle(
        ListMyDuesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<CollectionResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var found = await collections.ListDueForPayerAsync(memberId, cancellationToken);

        return Result.Success<IReadOnlyList<CollectionResponse>>([.. found.Select(c => c.ToResponse())]);
    }
}
