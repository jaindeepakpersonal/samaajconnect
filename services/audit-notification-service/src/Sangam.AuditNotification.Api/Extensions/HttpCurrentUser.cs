using System.Security.Claims;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Infrastructure.Security;

namespace Sangam.AuditNotification.Api.Extensions;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly HashSet<string> _roles;
    private readonly HashSet<string> _permissions;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;

        IsAuthenticated = principal?.Identity?.IsAuthenticated ?? false;

        _roles = principal is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : principal.FindAll(ClaimTypes.Role)
                .Concat(principal.FindAll(PlatformClaimTypes.Role))
                .Select(c => c.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _permissions = principal is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : principal.FindAll(PlatformClaimTypes.Permission)
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
