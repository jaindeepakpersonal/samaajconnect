namespace Sangam.Competition.Domain.Competitions;

/// <summary>
/// A member the organiser has named a judge for this competition. Owned by
/// <see cref="Competition"/>. Grants no platform role - the same "a judge is
/// just a member" reasoning volunteer-groups-service already established for
/// its own group lead.
/// </summary>
public sealed class CompetitionJudge
{
    public Guid Id { get; private set; }
    public Guid CompetitionId { get; private set; }
    public Guid MemberId { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }

    private CompetitionJudge() { }   // EF Core

    internal CompetitionJudge(Guid competitionId, Guid memberId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CompetitionId = competitionId;
        MemberId = memberId;
        AssignedAt = now;
    }
}
