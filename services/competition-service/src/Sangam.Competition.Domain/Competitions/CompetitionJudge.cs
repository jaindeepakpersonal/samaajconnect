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

    /// <summary>
    /// What this judge marks - the audition, the main rounds, or both. Main for
    /// every judge before 2026-10-25, and for every judge of a competition with
    /// no audition.
    /// </summary>
    public JudgePanel Panel { get; private set; } = JudgePanel.Main;

    public DateTimeOffset AssignedAt { get; private set; }

    private CompetitionJudge() { }   // EF Core

    internal CompetitionJudge(Guid competitionId, Guid memberId, JudgePanel panel, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CompetitionId = competitionId;
        MemberId = memberId;
        Panel = panel;
        AssignedAt = now;
    }

    internal void MoveTo(JudgePanel panel) => Panel = panel;

    /// <summary>Whether this judge marks rounds of this kind.</summary>
    public bool Judges(RoundKind kind) =>
        Panel == JudgePanel.Both
        || (kind == RoundKind.Audition ? Panel == JudgePanel.Audition : Panel == JudgePanel.Main);
}
