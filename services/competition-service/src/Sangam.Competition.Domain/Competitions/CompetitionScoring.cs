namespace Sangam.Competition.Domain.Competitions;

/// <summary>
/// The two rules that turn judges' marks into decisions: who goes through
/// from the audition, and who wins. Pure functions over numbers already
/// gathered, so the rules can be tested without a database.
/// </summary>
public static class CompetitionScoring
{
    /// <summary>
    /// The audition's finalists: the top <paramref name="finalistCount"/> by
    /// average mark, and everyone tied with the last of them.
    /// </summary>
    /// <remarks>
    /// A participant no judge marked did not audition, and never goes through
    /// - even when fewer than N were marked. Ties at the cut-off all go
    /// through rather than being split by something nobody chose, so the
    /// finalists can be more than N, never fewer than the marked allow.
    /// </remarks>
    public static IReadOnlyList<Guid> SelectFinalists(
        IReadOnlyList<(Guid MemberId, double? Average)> auditioned, int finalistCount)
    {
        var marked = auditioned
            .Where(a => a.Average is not null)
            .OrderByDescending(a => a.Average)
            .ToList();

        if (finalistCount <= 0 || marked.Count == 0)
        {
            return [];
        }

        if (marked.Count <= finalistCount)
        {
            return [.. marked.Select(a => a.MemberId)];
        }

        var cutOff = marked[finalistCount - 1].Average!.Value;

        return [.. marked.Where(a => a.Average >= cutOff).Select(a => a.MemberId)];
    }

    /// <summary>
    /// Standings, best first: each participant's total of their round
    /// averages, ranked so that equal totals share a place - 1, 2, 2, 4.
    /// </summary>
    /// <remarks>
    /// Every round counts equally, the user's choice (2026-10-25). An equal
    /// total is an equal result: two participants sharing second place is the
    /// honest answer, and splitting them by anything else would be deciding a
    /// prize by a rule no judge applied. <paramref name="order"/> only settles
    /// the order they are listed in.
    /// </remarks>
    public static IReadOnlyList<Standing> Rank(IEnumerable<(Guid MemberId, double Total, long Order)> totals)
    {
        // Rounded before comparing: two totals that are the same sum of the
        // same marks must not differ by a floating-point last digit.
        var ordered = totals
            .Select(t => (t.MemberId, Total: Math.Round(t.Total, 4), t.Order))
            .OrderByDescending(t => t.Total)
            .ThenBy(t => t.Order)
            .ToList();

        var standings = new List<Standing>(ordered.Count);

        for (var index = 0; index < ordered.Count; index++)
        {
            var (memberId, total, _) = ordered[index];
            var rank = index > 0 && ordered[index - 1].Total == total ? standings[index - 1].Rank : index + 1;

            standings.Add(new Standing(rank, memberId, total));
        }

        return standings;
    }

    /// <summary>The podium: everyone placed first, second or third, ties included.</summary>
    public static IReadOnlyList<Standing> Winners(IReadOnlyList<Standing> standings) =>
        [.. standings.Where(s => s.Rank <= 3)];
}

public sealed record Standing(int Rank, Guid MemberId, double Total);
