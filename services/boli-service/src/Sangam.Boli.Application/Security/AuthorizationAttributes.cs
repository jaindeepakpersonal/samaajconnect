namespace Sangam.Boli.Application.Security;

/// <summary>
/// Declares which roles may invoke a command or query.
/// TenantAuthorizationBehavior reads this off the request type.
/// </summary>
/// <remarks>
/// A request carrying none of these attributes is <b>denied</b>, not allowed.
/// Fail-closed is the point: forgetting to annotate a new command produces a
/// 403 in the first test run rather than an unguarded endpoint in production.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequiresRolesAttribute(params string[] roles) : Attribute
{
    public string[] Roles { get; } = roles;
}

/// <summary>Declares which permission key a command or query requires.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequiresPermissionAttribute(string permissionKey) : Attribute
{
    public string PermissionKey { get; } = permissionKey;
}

/// <summary>
/// Marks a request as deliberately reachable without authentication — slug
/// resolution, registration, login. Deliberate and greppable, so an
/// unauthenticated surface is always an explicit choice someone can audit.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllowAnonymousRequestAttribute : Attribute;

/// <summary>
/// Marks a request that this service raises for itself, from a Kafka consumer
/// rather than from an HTTP endpoint.
/// </summary>
/// <remarks>
/// Distinct from <see cref="AllowAnonymousRequestAttribute"/> on purpose.
/// "Anonymous" means a real caller reached us without a token; this means there
/// is no caller at all. Keeping them separate makes the genuinely
/// externally-reachable unauthenticated surface greppable on its own, which is
/// the list a security review actually wants.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class InternalRequestAttribute : Attribute;
