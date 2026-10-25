namespace Sangam.Competition.Domain.Competitions;

/// <summary>
/// One judged round of a competition. Owned by <see cref="Competition"/>.
/// </summary>
/// <remarks>
/// Made from the competition's format when judging starts (2026-10-25): an
/// Audition round first when there is one, then the main rounds - see
/// <see cref="Competition"/>'s SetUpRounds. "Does an entry exist for this
/// round" is what says a participant is in it: everybody who applied is
/// entered when judging starts, and the audition's finalists reach every
/// main round when it closes.
/// </remarks>
public sealed class Round
{
    public Guid Id { get; private set; }
    public Guid CompetitionId { get; private set; }
    public string Name { get; private set; } = null!;
    public int SequenceNumber { get; private set; }
    public RoundStatus Status { get; private set; }

    /// <summary>The audition, or a round of the competition itself. Every round before 2026-10-25 is Main.</summary>
    public RoundKind Kind { get; private set; } = RoundKind.Main;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    private Round() { }   // EF Core

    internal Round(
        Guid competitionId, string name, int sequenceNumber, DateTimeOffset now, RoundKind kind = RoundKind.Main)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.NewGuid();
        CompetitionId = competitionId;
        Name = name.Trim();
        SequenceNumber = sequenceNumber;
        Kind = kind;
        Status = RoundStatus.Open;
        CreatedAt = now;
    }

    internal void Close(DateTimeOffset now)
    {
        Status = RoundStatus.Closed;
        ClosedAt = now;
    }
}

public enum RoundStatus
{
    /// <summary>Accepting entries and scores.</summary>
    Open = 1,

    /// <summary>No new entries or scores. May now be published.</summary>
    Closed = 2,
}
