namespace Sangam.Competition.Domain.Competitions;

/// <summary>
/// How a competition is run, chosen when it is set up (2026-10-25): how many
/// judged rounds, the most a judge may give, and whether an audition comes
/// first.
/// </summary>
/// <param name="RoundCount">
/// Main rounds. Zero is a competition with no rounds to speak of - one
/// judging, shown to members as its top three and nothing else - and is held
/// as a single round named "Final" so scoring has one shape everywhere.
/// </param>
/// <param name="MaxMarks">The most one judge may give one participant in one round.</param>
/// <param name="AuditionRequired">Whether an audition decides who takes part in the main rounds.</param>
/// <param name="AuditionDate">When the audition is held. Required with an audition.</param>
/// <param name="FinalistCount">
/// How many go through from the audition - the top N by average audition
/// mark, and everyone tied with the Nth. Required with an audition.
/// </param>
public sealed record CompetitionFormat(
    int RoundCount,
    int MaxMarks,
    bool AuditionRequired,
    DateOnly? AuditionDate,
    int? FinalistCount)
{
    /// <summary>One judging out of 100, no audition - what every competition was before this existed.</summary>
    public static CompetitionFormat Default { get; } = new(0, 100, false, null, null);

    public const int MaxRounds = 20;

    public const int HighestMaxMarks = 1000;
}

/// <summary>Which panel a round is judged by.</summary>
public enum RoundKind
{
    /// <summary>Decides who takes part in the main rounds.</summary>
    Audition = 1,

    /// <summary>A round of the competition itself. Every finalist takes part in every one.</summary>
    Main = 2,
}

/// <summary>What a judge judges, when the competition has an audition.</summary>
public enum JudgePanel
{
    Main = 1,
    Audition = 2,
    Both = 3,
}
