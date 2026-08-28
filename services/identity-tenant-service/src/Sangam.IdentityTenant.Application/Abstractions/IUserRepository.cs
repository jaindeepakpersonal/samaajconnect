using Sangam.IdentityTenant.Domain.Users;

namespace Sangam.IdentityTenant.Application.Abstractions;

public interface IUserRepository
{
    /// <summary>
    /// Looks up a login by its identifier across <b>every</b> tenant, bypassing
    /// the global query filter.
    /// </summary>
    /// <remarks>
    /// This is the one deliberate exception to CLAUDE.md section 6, and it
    /// exists because the member portal offers a common login: the caller has
    /// not been routed to a Samaaj yet, so there is no tenant to filter by. The
    /// tenant is *derived* from the user that is found, never supplied by the
    /// caller. Do not add a second method that ignores the filter without the
    /// same kind of justification.
    /// </remarks>
    Task<User?> FindForLoginAsync(string mobileOrEmail, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller reading their own account, bypassing the tenant filter.
    /// </summary>
    /// <remarks>
    /// Narrow by design and used only by GetCurrentUserQuery. A Super Admin
    /// acting on a Samaaj through the gateway override has that Samaaj in
    /// ITenantContext, while their own account sits at User.PlatformTenantId -
    /// so the filtered lookup finds nothing and "who am I?" answers 404 the
    /// moment a platform admin starts administering anything.
    ///
    /// Bypassing the filter is safe here in a way it would not be elsewhere:
    /// the id is the subject of the validated token, never a value the caller
    /// supplied, so this can only ever return the caller to themselves. Do not
    /// reuse it for a lookup whose id comes from a route or a body.
    /// </remarks>
    Task<User?> GetSelfAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The account created for a converted child, if one exists. Ignores the
    /// tenant filter for the same reason as the login lookup: the consumer that
    /// calls it has no request and so no resolved tenant.
    /// </summary>
    Task<User?> GetByConvertedChildAsync(
        Guid childProfileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accounts in this Samaaj holding any of these roles, with their roles
    /// loaded. Tenant-filtered, like every read path reachable over HTTP.
    /// </summary>
    Task<IReadOnlyList<User>> ListWithRolesAsync(
        IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default);

    /// <summary>Accounts in this Samaaj waiting to be activated.</summary>
    Task<IReadOnlyList<User>> ListPendingActivationAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds an account awaiting activation by its identifier, across tenants.</summary>
    Task<User?> FindPendingActivationAsync(
        string mobileOrEmail, CancellationToken cancellationToken = default);

    /// <summary>True if the identifier is taken anywhere on the platform.</summary>
    Task<bool> IdentifierExistsAsync(string mobileOrEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// The role names and permission keys this user holds for one tenant,
    /// resolved through UserRole and RolePermission.
    /// </summary>
    Task<UserAuthorization> GetAuthorizationAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

    void Add(User user);
}

public sealed record UserAuthorization(
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions)
{
    public static UserAuthorization Empty { get; } = new([], []);
}
