using Microsoft.EntityFrameworkCore;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Persistence;

namespace Sangam.MemberFamily.Infrastructure.Repositories;

public sealed class MemberProfileRepository(MemberFamilyDbContext dbContext) : IMemberProfileRepository
{
    public Task<MemberProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MemberProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MemberProfiles
            // The consumer has no request and so no tenant; a filtered check
            // would match nothing and every redelivery would add a profile.
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MemberProfile>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // Tenant-filtered: an id from another Samaaj is simply not found.
        return await dbContext.MemberProfiles
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MemberProfile>> SearchAsync(
        MemberSearchFilter filter,
        ProfileViewer viewer,
        int skip,
        int limit,
        bool includeUnlisted,
        CancellationToken cancellationToken = default)
    {
        var (term, localityId, gautraId, professionId) = filter;

        // Tenant-filtered, never IgnoreQueryFilters: this is the one query a
        // signed-in member can drive with their own input.
        var query = dbContext.MemberProfiles.AsNoTracking();

        if (!includeUnlisted)
        {
            // The only thing IsListedInDirectory does. It is not an access
            // control: GetByIdAsync still returns an unlisted member, because a
            // group's president has to see who applied and a post has an author.
            // It also takes erased profiles out of the directory, which used to
            // rely on every field being null and the row still being listed.
            query = query.Where(p => p.IsListedInDirectory);
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";

            // Name only. Matching a private mobile number here would let anyone
            // confirm one digit-guess at a time, whatever the field's privacy
            // level said afterwards.
            query = query.Where(p => EF.Functions.ILike(p.FullName, pattern));
        }

        if (localityId is { } id)
        {
            // Exact, not a substring: Locality is a master-data-service pick
            // now, not text somebody typed toward.
            query = query.Where(p => p.PermanentAddress.LocalityId == id);
        }

        if (gautraId is { } gautra)
        {
            // No privacy level to respect: Gautra is shown on every row, like
            // the name (MemberMappings.ToDirectoryResponse).
            query = query.Where(p => p.GautraId == gautra);
        }

        if (professionId is { } profession)
        {
            query = query.Where(p => p.ProfessionId == profession);

            // Only professions this viewer could already read on the row -
            // the same rule as MemberProfile.IsVisibleTo. Without it, asking
            // for "CA" and seeing who comes back would confirm a Private value.
            if (!viewer.IsSamaajAdmin)
            {
                var self = viewer.UserId;

                query = query.Where(p => p.Privacy.Profession != PrivacyLevel.Private || p.Id == self);
            }
        }

        return await query
            .OrderBy(p => p.FullName)
            // Id breaks ties between two people with one name, so a page
            // boundary between them is stable rather than whatever order
            // Postgres happens to return.
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<MemberProfile?> GetForConsumerAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MemberProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<int> CountCreatedBetweenAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        dbContext.MemberProfiles
            .AsNoTracking()
            .CountAsync(p => p.CreatedAt >= from && p.CreatedAt < to, cancellationToken);

    public void Add(MemberProfile profile) => dbContext.MemberProfiles.Add(profile);
}
