using Sangam.Matrimony.Domain.Common;

namespace Sangam.Matrimony.Domain.Biodata;

/// <summary>
/// Whether a member may keep a biodata here - this service's copy of the one
/// fact it needs from member-family-service's marital status (2026-10-24).
/// </summary>
/// <remarks>
/// <para>
/// <b>Matrimony is for members who are unmarried or divorced</b> (the user's
/// rule). The status lives on the member's profile in member-family-service,
/// and this repo reaches across a service boundary by event, never by a
/// synchronous call - so <c>members.marital-status.changed.v1</c> carries
/// <see cref="IsEligible"/> and this row keeps it. Not the status itself: a
/// yes or no is all the rule acts on.
/// </para>
/// <para>
/// <b>No row is "not known", and not known is not eligible.</b> A member who has
/// not set their marital status cannot opt in, edit a biodata, or appear in a
/// search until they do; the page tells them where to set it.
/// </para>
/// <para>
/// <b>An older change never overwrites a newer one.</b> Kafka delivers at least
/// once and a member can change their status twice in a minute, so
/// <see cref="Apply"/> keeps whichever change happened last.
/// </para>
/// </remarks>
public sealed class MatrimonyEligibility : ITenantScopedEntity
{
    /// <summary>The member's own id, as everywhere on this platform.</summary>
    public Guid MemberId { get; private set; }

    public Guid TenantId { get; private set; }

    public bool IsEligible { get; private set; }

    /// <summary>When member-family-service recorded the change this row reflects.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    private MatrimonyEligibility() { }   // EF Core

    public static MatrimonyEligibility Record(Guid memberId, Guid tenantId, bool isEligible, DateTimeOffset changedAt) =>
        new() { MemberId = memberId, TenantId = tenantId, IsEligible = isEligible, ChangedAt = changedAt };

    /// <summary>Takes a change that is newer than the one held. False for a stale or repeated one.</summary>
    public bool Apply(bool isEligible, DateTimeOffset changedAt)
    {
        if (changedAt <= ChangedAt)
        {
            return false;
        }

        IsEligible = isEligible;
        ChangedAt = changedAt;

        return true;
    }
}
