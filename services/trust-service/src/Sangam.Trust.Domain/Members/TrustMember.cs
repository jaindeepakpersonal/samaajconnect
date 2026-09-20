using Sangam.Trust.Domain.Common;

namespace Sangam.Trust.Domain.Members;

/// <summary>
/// One member's seat on a Trust's roster.
/// </summary>
/// <remarks>
/// Its own aggregate root, not owned by <see cref="Trusts.Trust"/> - the same
/// reasoning <c>panch-service</c>'s <c>PanelMember</c> gives for standing apart
/// from its election: so that adding or removing one raises its own event,
/// through its own outbox row, rather than requiring the whole Trust to be
/// loaded and saved for a roster change.
///
/// There is at most one row per (TrustId, MemberId) ever - see
/// <see cref="Reactivate"/>. Re-adding somebody previously removed reactivates
/// their old row instead of inserting a second one, which is what keeps the
/// unique index meaningful as a permanent record of "has this person ever been
/// on this Trust's roster" rather than merely "are they on it right now".
/// </remarks>
public sealed class TrustMember : AggregateRoot, ITenantScopedEntity
{
    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid TrustId { get; private set; }

    /// <summary>The member holding this seat. Names live in member-family-service.</summary>
    public Guid MemberId { get; private set; }

    /// <summary>
    /// Free text - "President", "Treasurer" - grants nothing by itself, the
    /// same stance <c>volunteer-groups-service</c>'s <c>RolePosition</c> takes.
    /// There is no election yet in this phase, so it is only ever set by hand.
    /// </summary>
    public string? PositionTitle { get; private set; }

    public TrustMemberStatus Status { get; private set; }

    public Guid AddedBy { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public Guid? RemovedBy { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    private TrustMember() { }   // EF Core

    public static TrustMember Create(
        Guid tenantId, Guid trustId, Guid memberId, string? positionTitle, Guid addedBy, DateTimeOffset now)
    {
        var member = new TrustMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TrustId = trustId,
            MemberId = memberId,
            PositionTitle = Normalize(positionTitle),
            Status = TrustMemberStatus.Active,
            AddedBy = addedBy,
            AddedAt = now,
        };

        member.Raise(new TrustMemberAddedDomainEvent(
            member.Id, tenantId, trustId, memberId, member.PositionTitle, addedBy, now));

        return member;
    }

    /// <summary>
    /// Brings a removed member back onto the roster on their existing row,
    /// rather than a second insert colliding with the unique index on
    /// (TrustId, MemberId).
    /// </summary>
    public void Reactivate(string? positionTitle, Guid addedBy, DateTimeOffset now)
    {
        PositionTitle = Normalize(positionTitle);
        Status = TrustMemberStatus.Active;
        AddedBy = addedBy;
        AddedAt = now;
        RemovedBy = null;
        RemovedAt = null;

        Raise(new TrustMemberAddedDomainEvent(Id, TenantId, TrustId, MemberId, PositionTitle, addedBy, now));
    }

    /// <summary>False when this member is not currently active - an update has nothing to update.</summary>
    public bool UpdatePosition(string? positionTitle, Guid updatedBy, DateTimeOffset now)
    {
        if (Status != TrustMemberStatus.Active)
        {
            return false;
        }

        PositionTitle = Normalize(positionTitle);

        Raise(new TrustMemberUpdatedDomainEvent(Id, TenantId, TrustId, MemberId, PositionTitle, updatedBy, now));

        return true;
    }

    /// <summary>False when already removed - a repeated removal is a no-op, not a second fact.</summary>
    public bool Remove(Guid removedBy, DateTimeOffset now)
    {
        if (Status != TrustMemberStatus.Active)
        {
            return false;
        }

        Status = TrustMemberStatus.Removed;
        RemovedBy = removedBy;
        RemovedAt = now;

        Raise(new TrustMemberRemovedDomainEvent(Id, TenantId, TrustId, MemberId, removedBy, now));

        return true;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public enum TrustMemberStatus
{
    Active = 1,
    Removed = 2,
}
