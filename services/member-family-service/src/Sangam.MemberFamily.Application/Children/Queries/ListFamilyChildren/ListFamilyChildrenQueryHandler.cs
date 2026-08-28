using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;

namespace Sangam.MemberFamily.Application.Children.Queries.ListFamilyChildren;

public sealed class ListFamilyChildrenQueryHandler(
    IChildRepository children,
    IChildConversionRepository conversions,
    IFamilyRepository families,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : IRequestHandler<ListFamilyChildrenQuery, Result<IReadOnlyList<ChildResponse>>>
{
    public async Task<Result<IReadOnlyList<ChildResponse>>> Handle(
        ListFamilyChildrenQuery query,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } memberId)
        {
            return Result.Failure<IReadOnlyList<ChildResponse>>(
                Error.Unauthorized("Auth.Required", "Authentication is required for this request."));
        }

        var family = await families.GetForMemberAsync(memberId, cancellationToken);

        if (family is null)
        {
            // No family is not an error; it is the normal state of a member who
            // has not created or joined one.
            return Result.Success<IReadOnlyList<ChildResponse>>([]);
        }

        var found = await children.ListForFamilyAsync(family.Id, cancellationToken);
        var pending = await conversions.ListPendingAsync(cancellationToken);
        var pendingChildIds = pending.Select(r => r.ChildProfileId).ToHashSet();
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        IReadOnlyList<ChildResponse> results = found
            .Select(child => child.ToResponse(today, pendingChildIds.Contains(child.Id)))
            .ToList();

        return Result.Success(results);
    }
}
