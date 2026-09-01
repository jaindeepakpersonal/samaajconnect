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

    public async Task<IReadOnlyList<MemberProfile>> SearchAsync(
        string? term,
        string? locality,
        int limit,
        bool includeUnlisted,
        CancellationToken cancellationToken = default)
    {
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

        if (!string.IsNullOrWhiteSpace(locality))
        {
            var pattern = $"%{locality.Trim()}%";

            query = query.Where(p => p.Locality != null && EF.Functions.ILike(p.Locality, pattern));
        }

        return await query
            .OrderBy(p => p.FullName)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public Task<MemberProfile?> GetForConsumerAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.MemberProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public void Add(MemberProfile profile) => dbContext.MemberProfiles.Add(profile);
}
