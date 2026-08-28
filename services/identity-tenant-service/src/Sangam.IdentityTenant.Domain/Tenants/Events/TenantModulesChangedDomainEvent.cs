using Sangam.IdentityTenant.Domain.Common;

namespace Sangam.IdentityTenant.Domain.Tenants;

/// <summary>
/// A Samaaj switched a module on or off.
/// </summary>
/// <remarks>
/// Carries both sets rather than a delta. The gateway caches what a Samaaj
/// runs, and a consumer that has missed a message needs to be able to correct
/// itself from one event rather than replay every change since it last agreed.
/// Turning a module off makes a whole area of the platform answer 404 for that
/// Samaaj, so the previous set is worth having in the audit log too.
///
/// Nothing consumes this today. The gateway re-reads the Samaaj when its own
/// 60-second cache expires rather than following events, so a module change
/// takes effect within a minute with no consumer to keep in step.
/// </remarks>
public sealed record TenantModulesChangedDomainEvent(
    Guid TenantId,
    IReadOnlyCollection<string> PreviousModules,
    IReadOnlyCollection<string> EnabledModules,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    public string Topic => "identity.tenant.modules-changed.v1";
}
