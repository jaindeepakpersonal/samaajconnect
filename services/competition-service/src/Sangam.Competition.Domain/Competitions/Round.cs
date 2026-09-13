namespace Sangam.Competition.Domain.Competitions;

/// <summary>
/// One judged round of a competition. Owned by <see cref="Competition"/>.
/// </summary>
/// <remarks>
/// There is no automatic advancement between rounds. The organiser creates
/// one <see cref="Entries.Entry"/> per participant they want in a given
/// round, by hand, for every round including the first - a simple
/// one-round competition just has one round with an entry for everyone who
/// registered, and an elimination format has the organiser create fewer
/// entries in the next round after reviewing the previous one's published
/// result. "Does an entry exist for this round" already answers "is this
/// participant still in it," so there is nothing an automatic top-N cutoff
/// would buy that is worth its own edge cases (a tie at the cutoff, a
/// participant who withdraws mid-competition).
/// </remarks>
public sealed class Round
{
    public Guid Id { get; private set; }
    public Guid CompetitionId { get; private set; }
    public string Name { get; private set; } = null!;
    public int SequenceNumber { get; private set; }
    public RoundStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    private Round() { }   // EF Core

    internal Round(Guid competitionId, string name, int sequenceNumber, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.NewGuid();
        CompetitionId = competitionId;
        Name = name.Trim();
        SequenceNumber = sequenceNumber;
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
