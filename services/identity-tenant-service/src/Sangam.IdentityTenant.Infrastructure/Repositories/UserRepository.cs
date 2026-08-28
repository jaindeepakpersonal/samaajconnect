using Microsoft.EntityFrameworkCore;
using Sangam.IdentityTenant.Application.Abstractions;
using Sangam.IdentityTenant.Domain.Users;
using Sangam.IdentityTenant.Infrastructure.Persistence;

namespace Sangam.IdentityTenant.Infrastructure.Repositories;

public sealed class UserRepository(IdentityTenantDbContext dbContext) : IUserRepository
{
    public Task<User?> FindForLoginAsync(string mobileOrEmail, CancellationToken cancellationToken = default) =>
        dbContext.Users
            // Deliberate, and the only place in this service it is done. Login
            // happens before any Samaaj has been resolved, so there is no tenant
            // to filter on; the tenant is read off whichever user is found.
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.MobileOrEmail == mobileOrEmail, cancellationToken);

    /// <summary>
    /// Tenant-filtered, with the role grants loaded.
    /// </summary>
    /// <remarks>
    /// The Include is load-bearing, not an optimisation. Every write path that
    /// reaches a User through this method reasons about its roles - granting
    /// one checks for a duplicate, revoking one looks for the grant, and
    /// erasure clears them all. With the collection unloaded each of those
    /// operates on an empty list and reports success having done nothing.
    /// </remarks>
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetSelfAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<bool> IdentifierExistsAsync(string mobileOrEmail, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.MobileOrEmail == mobileOrEmail, cancellationToken);

    public async Task<UserAuthorization> GetAuthorizationAsync(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // A grant applies when it is scoped to this tenant or is platform-wide
        // (null scope, i.e. Super Admin).
        var roleIds = await dbContext.UserRoles
            .Where(ur => ur.UserId == userId && (ur.TenantScope == null || ur.TenantScope == tenantId))
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            return UserAuthorization.Empty;
        }

        var roles = await dbContext.Roles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var permissions = await dbContext.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(dbContext.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p.Key)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new UserAuthorization(roles, permissions);
    }

    public Task<User?> GetByConvertedChildAsync(
        Guid childProfileId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            // The consumer that calls this has no request and so no tenant.
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.ConvertedFromChildProfileId == childProfileId, cancellationToken);

    public async Task<IReadOnlyList<User>> ListWithRolesAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default) =>
        // Tenant-filtered: reachable over HTTP, so a Samaaj Admin sees their own
        // administrators and nobody else's.
        await dbContext.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .Where(u => u.Status != UserStatus.Erased)
            .Where(u => u.Roles.Any(r => roleIds.Contains(r.RoleId)))
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<User>> ListPendingActivationAsync(
        CancellationToken cancellationToken = default) =>
        // Tenant-filtered: this one is reachable over HTTP.
        await dbContext.Users
            .AsNoTracking()
            .Include(u => u.ActivationCode)
            .Where(u => u.Status == UserStatus.PendingActivation)
            .OrderBy(u => u.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<User?> FindPendingActivationAsync(
        string mobileOrEmail, CancellationToken cancellationToken = default) =>
        dbContext.Users
            // Activation happens before the caller can sign in, so like login
            // there is no tenant to filter by yet. The tenant is read off
            // whichever account is found, never supplied.
            .IgnoreQueryFilters()
            .Include(u => u.ActivationCode)
            .FirstOrDefaultAsync(
                u => u.MobileOrEmail == mobileOrEmail && u.Status == UserStatus.PendingActivation,
                cancellationToken);

    public void Add(User user) => dbContext.Users.Add(user);
}
