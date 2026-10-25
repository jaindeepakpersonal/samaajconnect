using Sangam.Competition.Domain.Common;

namespace Sangam.Competition.Domain.Entries;

/// <summary>
/// One participant's place in one round.
/// </summary>
/// <remarks>
/// A separate, tenant-scoped aggregate - not owned by <c>Competition</c> or
/// <c>Round</c> - for the same "don't let the busy child balloon the
/// parent" reasoning that keeps businesses-service's <c>Order</c> its own
/// aggregate rather than nested under <c>Business</c>. Made for everybody
/// who applied when judging starts, for the finalists when the audition
/// closes, and by the organiser for somebody added late (see <c>Round</c>).
/// </remarks>
public sealed class Entry : ITenantScopedEntity
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RoundId { get; private set; }
    public Guid ParticipantMemberId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Entry() { }   // EF Core

    public Entry(Guid tenantId, Guid roundId, Guid participantMemberId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        RoundId = roundId;
        ParticipantMemberId = participantMemberId;
        CreatedAt = now;
    }
}
