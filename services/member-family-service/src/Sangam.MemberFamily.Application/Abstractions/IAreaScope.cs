namespace Sangam.MemberFamily.Application.Abstractions;

/// <summary>
/// An Area Admin's restriction to one locality, read off the validated token's
/// <c>locality_id</c> claim (identity-tenant-service's UserRole.LocalityId).
/// </summary>
/// <remarks>
/// <para>
/// Its own interface rather than a member of <see cref="ITenantContext"/>,
/// because that file is a verbatim copy in every service and
/// <c>scripts/security-invariants.sh</c> holds it so.
/// </para>
/// <para>
/// <b>It bounds administrative powers, never membership.</b> An Area Admin is
/// also a member of their Samaaj, and reads and acts as one everywhere - the
/// directory, events, groups, issues. What the locality limits is the
/// administration: seeing past privacy levels and unlisted members,
/// correcting someone's details, and (in the other services) setting up,
/// publishing, approving. A filter that hid the rest of the Samaaj from them
/// would have taken away their membership to scope their admin role.
/// </para>
/// </remarks>
public interface IAreaScope
{
    /// <summary>Set only for an Area Admin; null for everyone else.</summary>
    Guid? AreaLocalityId { get; }
}

public static class AreaScopeExtensions
{
    /// <summary>
    /// True when the caller is an Area Admin and this locality is theirs. Null
    /// is nobody's locality: a record with none set is administered by the
    /// Samaaj Admin alone.
    /// </summary>
    public static bool AdministersLocality(this IAreaScope scope, Guid? localityId) =>
        scope.AreaLocalityId is { } own && localityId == own;

    /// <summary>
    /// False only for an Area Admin acting outside their locality - the check
    /// every admin-permission handler makes after the permission itself.
    /// </summary>
    public static bool MayAdminister(this IAreaScope scope, Guid? localityId) =>
        scope.AreaLocalityId is null || localityId == scope.AreaLocalityId;
}
