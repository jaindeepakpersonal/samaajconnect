namespace Sangam.IdentityTenant.Application.Abstractions;

/// <summary>
/// The authenticated caller, read from validated JWT claims. Services re-check
/// roles and permissions themselves rather than trusting the gateway alone
/// (ARCHITECTURE.md §6, defense in depth).
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAuthenticated { get; }

    IReadOnlyCollection<string> Roles { get; }

    IReadOnlyCollection<string> Permissions { get; }

    bool HasPermission(string permissionKey);

    bool IsInRole(string role);
}
