using Sangam.SocialIssues.Domain.Common;

namespace Sangam.SocialIssues.Domain.Panel;

/// <summary>
/// One member of a Samaaj's Panch, as this service remembers it.
/// </summary>
/// <remarks>
/// A local copy, fed by <c>panch.panel.changed.v1</c>. panch-service owns who
/// sits on the Panch; this service only needs to know, on every review and every
/// decision, whether the caller does - and asking panch-service on each one
/// would tie every issue's workflow to that service being up.
///
/// The event carries the whole roster, so a change <i>replaces</i> a Samaaj's
/// rows here rather than adding or removing one. That is what makes redelivery
/// harmless: applying the same snapshot twice leaves the same rows.
///
/// Not an aggregate: nothing here raises an event.
/// </remarks>
public sealed class PanelMember : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid MemberId { get; private set; }

    private PanelMember() { }   // EF Core

    public PanelMember(Guid tenantId, Guid memberId)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        MemberId = memberId;
    }
}
