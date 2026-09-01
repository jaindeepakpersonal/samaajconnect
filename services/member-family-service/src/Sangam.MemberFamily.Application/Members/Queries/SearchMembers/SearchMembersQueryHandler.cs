using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members.Queries.SearchMembers;

public sealed class SearchMembersQueryHandler(
    IMemberProfileRepository profiles,
    ICurrentUser currentUser)
    : IRequestHandler<SearchMembersQuery, Result<IReadOnlyList<MemberResponse>>>
{
    public async Task<Result<IReadOnlyList<MemberResponse>>> Handle(
        SearchMembersQuery query,
        CancellationToken cancellationToken)
    {
        var isAdmin = currentUser.IsInRole(Roles.SamaajAdmin) || currentUser.IsInRole(Roles.SuperAdmin);

        var found = await profiles.SearchAsync(
            query.Term,
            query.Locality,
            Math.Clamp(query.Limit, 1, 100),
            // Administrators see members who have taken themselves out of the
            // directory; nobody else does. The same role decides whether the
            // privacy levels are seen through, and for the same reason:
            // correcting a member's details is administrative work.
            includeUnlisted: isAdmin,
            cancellationToken);

        var viewer = new ProfileViewer(currentUser.UserId, isAdmin);

        IReadOnlyList<MemberResponse> results = found
            .Select(profile => profile.ToDirectoryResponse(viewer))
            .ToList();

        return Result.Success(results);
    }
}
