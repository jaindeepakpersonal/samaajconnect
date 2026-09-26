using MediatR;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Common;
using Sangam.MemberFamily.Application.Security;
using Sangam.MemberFamily.Domain.Members;

namespace Sangam.MemberFamily.Application.Members.Queries.SearchMembers;

public sealed class SearchMembersQueryHandler(
    IMemberProfileRepository profiles,
    ICurrentUser currentUser,
    IAreaScope areaScope)
    : IRequestHandler<SearchMembersQuery, Result<PagedResult<MemberResponse>>>
{
    public async Task<Result<PagedResult<MemberResponse>>> Handle(
        SearchMembersQuery query,
        CancellationToken cancellationToken)
    {
        // An Area Admin gets the administrator's view - unlisted members, every
        // privacy level seen through - only for a search confined to their own
        // locality, which is what the admin panel's Members screen sends for
        // them. Any other search is the member view, the directory they have
        // as a member of the Samaaj like anybody else (IAreaScope's remarks).
        var isAdmin = currentUser.IsInRole(Roles.SamaajAdmin)
            || currentUser.IsInRole(Roles.SuperAdmin)
            || areaScope.AdministersLocality(query.LocalityId);

        // One row more than asked for, so HasMore is known without a second
        // COUNT(*) query - the extra row is sliced off below and never reaches
        // the caller.
        var found = await profiles.SearchAsync(
            query.Term,
            query.LocalityId,
            query.Skip,
            query.Take + 1,
            // Administrators see members who have taken themselves out of the
            // directory; nobody else does. The same role decides whether the
            // privacy levels are seen through, and for the same reason:
            // correcting a member's details is administrative work.
            includeUnlisted: isAdmin,
            cancellationToken);

        var viewer = new ProfileViewer(currentUser.UserId, isAdmin);

        var hasMore = found.Count > query.Take;

        IReadOnlyList<MemberResponse> results = found
            .Take(query.Take)
            .Select(profile => profile.ToDirectoryResponse(viewer))
            .ToList();

        return Result.Success(new PagedResult<MemberResponse>(results, hasMore));
    }
}
