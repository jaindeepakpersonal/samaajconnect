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
