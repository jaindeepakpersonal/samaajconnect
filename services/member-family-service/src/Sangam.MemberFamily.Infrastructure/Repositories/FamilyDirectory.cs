using Microsoft.EntityFrameworkCore;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Families.Queries.ListFamilies;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Infrastructure.Persistence;

namespace Sangam.MemberFamily.Infrastructure.Repositories;

/// <summary>
/// Three tenant-filtered reads for one page: the families, their members'
/// profiles, their children. Never <c>dbContext.FamilyMembers</c> directly -
/// a member row carries no tenant and so has no query filter of its own; it is
/// reached only through its family, which does.
/// </summary>
public sealed class FamilyDirectory(MemberFamilyDbContext dbContext) : IFamilyDirectory
{
    public async Task<IReadOnlyList<FamilyOverviewResponse>> ListAsync(
        Guid? localityId, int skip, int take, DateOnly today, CancellationToken cancellationToken = default)
    {
        var families = dbContext.Families.AsNoTracking().Include(f => f.Members).AsQueryable();

        if (localityId is { } id)
        {
            var livingThere = dbContext.MemberProfiles
                .Where(p => p.PermanentAddress.LocalityId == id)
                .Select(p => p.Id);

            families = families.Where(f => f.Members.Any(m =>
                m.Status == FamilyMemberStatus.Active && livingThere.Contains(m.MemberProfileId)));
        }

        var page = await families
            .OrderByDescending(f => f.CreatedAt)
            .ThenBy(f => f.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return [];
        }

        var memberIds = page
            .SelectMany(f => f.Members)
            .Where(m => m.Status == FamilyMemberStatus.Active)
            .Select(m => m.MemberProfileId)
            .Distinct()
            .ToList();

        var profiles = await dbContext.MemberProfiles
            .AsNoTracking()
            .Where(p => memberIds.Contains(p.Id))
            .Select(p => new { p.Id, p.FullName, p.PermanentAddress.LocalityId })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var familyIds = page.Select(f => f.Id).ToList();

        var children = await dbContext.ChildProfiles
            .AsNoTracking()
            .Where(c => familyIds.Contains(c.FamilyId) && c.Status == ChildStatus.Minor)
            .OrderBy(c => c.DateOfBirth)
            .Select(c => new { c.Id, c.FamilyId, c.FullName, c.DateOfBirth, c.Gender })
            .ToListAsync(cancellationToken);

        return [.. page.Select(family => new FamilyOverviewResponse(
            family.Id,
            family.FamilyCode,
            family.FamilyHeadMemberId,
            family.CreatedAt,
            [.. family.Members
                .Where(m => m.Status == FamilyMemberStatus.Active)
                .OrderByDescending(m => m.MemberProfileId == family.FamilyHeadMemberId)
                .ThenBy(m => m.RequestedAt)
                .Select(m => new FamilyOverviewMember(
                    m.MemberProfileId,
                    profiles.TryGetValue(m.MemberProfileId, out var profile) ? profile.FullName : "A member",
                    m.Relationship.ToString(),
                    m.MemberProfileId == family.FamilyHeadMemberId,
                    profile?.LocalityId))],
            [.. children
                .Where(c => c.FamilyId == family.Id)
                .Select(c => new FamilyOverviewChild(c.Id, c.FullName, AgeOn(c.DateOfBirth, today), c.Gender.ToString()))]))];
    }

    private static int AgeOn(DateOnly born, DateOnly today)
    {
        var age = today.Year - born.Year;

        return born.AddYears(age) > today ? age - 1 : age;
    }
}
