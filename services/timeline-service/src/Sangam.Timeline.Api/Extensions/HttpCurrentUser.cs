using System.Security.Claims;
using Sangam.Timeline.Application.Abstractions;
using Sangam.Timeline.Infrastructure.Security;

namespace Sangam.Timeline.Api.Extensions;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly HashSet<string> _roles;
    private readonly HashSet<string> _permissions;

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;

        IsAuthenticated = principal?.Identity?.IsAuthenticated ?? false;

        // One claim name, not two. The JwtBearer options turn inbound claim
        // mapping off, so a role arrives named exactly as it was issued -
        // reading both names here would only hide it if that ever changed.
        _roles = principal is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : principal.FindAll(PlatformClaimTypes.Role)
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
