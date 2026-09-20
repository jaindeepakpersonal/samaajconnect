using MediatR;
using Sangam.Trust.Application.Abstractions;
using Sangam.Trust.Application.Common;
using Sangam.Trust.Application.Security;
using Sangam.Trust.Domain.Members;

namespace Sangam.Trust.Application.Members.Queries;

/// <summary>One Trust's roster, active members first.</summary>
[RequiresPermission(PermissionKeys.MembersRead)]
public sealed record ListTrustMembersQuery(Guid TrustId) : IQuery<IReadOnlyList<TrustMemberResponse>>;

public sealed class ListTrustMembersQueryHandler(
    ITrustRepository trusts,
    ITrustMemberRepository members)
    : IRequestHandler<ListTrustMembersQuery, Result<IReadOnlyList<TrustMemberResponse>>>
{
    public async Task<Result<IReadOnlyList<TrustMemberResponse>>> Handle(
        ListTrustMembersQuery query,
        CancellationToken cancellationToken)
    {
        var trust = await trusts.GetByIdAsync(query.TrustId, cancellationToken);

        if (trust is null)
        {
            return Result.Failure<IReadOnlyList<TrustMemberResponse>>(
                Error.NotFound("Trust.NotFound", "No such Trust in this Samaaj."));
        }

        var found = await members.ListByTrustAsync(trust.Id, cancellationToken);

        var ordered = found
            .OrderBy(m => m.Status == TrustMemberStatus.Active ? 0 : 1)
            .ThenBy(m => m.AddedAt);

        return Result.Success<IReadOnlyList<TrustMemberResponse>>([.. ordered.Select(m => m.ToResponse())]);
    }
}
