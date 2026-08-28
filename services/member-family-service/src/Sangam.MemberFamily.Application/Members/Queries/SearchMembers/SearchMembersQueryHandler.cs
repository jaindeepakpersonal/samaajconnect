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
        var found = await profiles.SearchAsync(
            query.Term, query.Locality, Math.Clamp(query.Limit, 1, 100), cancellationToken);

        var viewer = new ProfileViewer(
            currentUser.UserId,
            currentUser.IsInRole(Roles.SamaajAdmin) || currentUser.IsInRole(Roles.SuperAdmin));

        IReadOnlyList<MemberResponse> results = found
            .Select(profile => profile.ToDirectoryResponse(viewer))
            .ToList();

        return Result.Success(results);
    }
}
