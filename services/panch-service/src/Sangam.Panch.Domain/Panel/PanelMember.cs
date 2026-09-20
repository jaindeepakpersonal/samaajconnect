using Sangam.Panch.Domain.Common;
using Sangam.Panch.Domain.Elections;

namespace Sangam.Panch.Domain.Panel;

/// <summary>
/// One seated member of the Samaaj Panch. The rows for a tenant <i>are</i> the
/// panel: publishing an election replaces the lot, and an admin may remove one.
/// </summary>
/// <remarks>
/// There is no term. A panel stays seated until an election replaces it or an
/// admin removes a member, so this is a plain roster rather than a set of dated
/// appointments. A seat left empty by a removal stays empty until the next
/// election - filling it by hand would be an appointment nobody voted for.
/// It is an aggregate root only so that removing one can announce the new
/// roster through the same outbox as everything else.
/// </remarks>
public sealed class PanelMember : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid MemberId { get; private set; }

    /// <summary>The election that seated them.</summary>
    public Guid ElectionId { get; private set; }

    public DateTimeOffset SeatedAt { get; private set; }

    private PanelMember() { }   // EF Core

    public PanelMember(Guid tenantId, Guid memberId, Guid electionId, DateTimeOffset seatedAt)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        MemberId = memberId;
        ElectionId = electionId;
        SeatedAt = seatedAt;
    }

    /// <summary>
    /// Announces that this member is being taken off the panel, and who is
    /// left. The caller removes the row.
    /// </summary>
    public void AnnounceRemoval(IReadOnlyList<Guid> remainingMemberIds, Guid removedBy, DateTimeOffset now) =>
        Raise(new PanelChangedDomainEvent(TenantId, remainingMemberIds, "Removed", ElectionId, removedBy, now));
}
