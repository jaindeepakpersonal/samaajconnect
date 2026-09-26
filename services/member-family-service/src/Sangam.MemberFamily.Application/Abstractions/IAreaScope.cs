namespace Sangam.MemberFamily.Application.Abstractions;

/// <summary>
/// An Area Admin's restriction to one locality, read off the validated token's
/// <c>locality_id</c> claim (identity-tenant-service's UserRole.LocalityId).
/// </summary>
/// <remarks>
/// Its own interface rather than a member of <see cref="ITenantContext"/>,
/// because that file is a verbatim copy in every service and
/// <c>scripts/security-invariants.sh</c> holds it so - and only this service
/// holds locality-scoped data yet. Every handler that reads or writes the
/// member directory treats it as an inner gate beside TenantId, never trusting
/// a caller-supplied locality filter over it.
/// </remarks>
public interface IAreaScope
{
    /// <summary>Set only for an Area Admin; null for everyone else.</summary>
    Guid? AreaLocalityId { get; }
}
