using System.Security.Claims;
using Sangam.IdentityTenant.Application.Abstractions;

namespace Sangam.IdentityTenant.Api.Extensions;

public sealed class HttpCurrentUser : ICurrentUser
{
    public const string PermissionClaimType = "permission";

    private readonly HashSet<string> _roles;
    private readonly HashSet<string> _permissions;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;

        IsAuthenticated = principal?.Identity?.IsAuthenticated ?? false;

        _roles = principal is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : principal.FindAll(ClaimTypes.Role)
                .Concat(principal.FindAll("role"))
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _permissions = principal is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : principal.FindAll(PermissionClaimType)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");

        if (Guid.TryParse(subject, out var userId))
        {
            UserId = userId;
        }
    }

    public Guid? UserId { get; }

    public bool IsAuthenticated { get; }

    public IReadOnlyCollection<string> Roles => _roles;

    public IReadOnlyCollection<string> Permissions => _permissions;

    public bool HasPermission(string permissionKey) => _permissions.Contains(permissionKey);

    public bool IsInRole(string role) => _roles.Contains(role);
}
