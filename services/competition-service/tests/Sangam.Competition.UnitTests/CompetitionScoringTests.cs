using Sangam.Competition.Domain.Competitions;
using Xunit;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.UnitTests;

/// <summary>
/// The two rules the user chose (2026-10-25): the top N go through from the
/// audition, and the winners are the highest totals of round averages.
/// </summary>
public sealed class CompetitionScoringTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_top_N_go_through_from_the_audition()
    {
        var finalists = CompetitionScoring.SelectFinalists([(A, 6.0), (B, 9.0), (C, 7.5), (D, 3.0)], 2);

        Assert.Equal([B, C], finalists);
    }

    [Fact]
    public void Everyone_tied_with_the_last_finalist_goes_through_too()
    {
        // Nobody chose a tie-break, so a tie at the cut-off is not split.
        var finalists = CompetitionScoring.SelectFinalists([(A, 8.0), (B, 9.0), (C, 8.0), (D, 3.0)], 2);

        Assert.Equal(3, finalists.Count);
        Assert.Contains(A, finalists);
        Assert.Contains(C, finalists);
        Assert.DoesNotContain(D, finalists);
    }

    [Fact]
    public void Somebody_no_judge_marked_did_not_audition_and_never_goes_through()
    {
        // Even with places to spare: an unmarked participant is not a zero.
        var finalists = CompetitionScoring.SelectFinalists([(A, 4.0), (B, null), (C, 2.0)], 5);

        Assert.Equal([A, C], finalists);
    }

    [Fact]
    public void Equal_totals_share_a_place_and_the_next_place_is_skipped()
    {
        var standings = CompetitionScoring.Rank([(A, 15.0, 1), (B, 18.0, 2), (C, 15.0, 3), (D, 9.0, 4)]);

        Assert.Equal([B, A, C, D], standings.Select(s => s.MemberId));
        Assert.Equal([1, 2, 2, 4], standings.Select(s => s.Rank));
    }

    [Fact]
    public void Totals_that_differ_only_by_floating_point_noise_are_equal()
    {
        var standings = CompetitionScoring.Rank([(A, 0.1 + 0.2, 1), (B, 0.3, 2)]);

        Assert.Equal(standings[0].Rank, standings[1].Rank);
    }

    [Fact]
    public void The_winners_are_everyone_placed_first_to_third_ties_included()
    {
        var standings = CompetitionScoring.Rank([(A, 20.0, 1), (B, 18.0, 2), (C, 15.0, 3), (D, 15.0, 4)]);

        Assert.Equal(4, CompetitionScoring.Winners(standings).Count);
    }

    // ---- Rounds come from the format when judging starts -------------------

    private static CompetitionAggregate StartJudging(CompetitionFormat format)
    {
        var competition = CompetitionAggregate.Create(
            Guid.NewGuid(), "Elocution", null, null, Now, Now.AddDays(7), Guid.NewGuid(), Now, format: format);

        competition.MoveTo(CompetitionStatus.RegistrationOpen, Now);
        competition.MoveTo(CompetitionStatus.RegistrationClosed, Now);
        competition.MoveTo(CompetitionStatus.InProgress, Now);

        return competition;
    }

    [Fact]
    public void No_rounds_is_one_judging_called_Final()
    {
        var competition = StartJudging(CompetitionFormat.Default);

        var round = Assert.Single(competition.Rounds);
        Assert.Equal("Final", round.Name);
        Assert.Equal(RoundKind.Main, round.Kind);
    }

    [Fact]
    public void An_audition_comes_first_then_each_main_round()
    {
        var competition = StartJudging(new CompetitionFormat(3, 10, true, new DateOnly(2026, 1, 10), 5));

        Assert.Equal(
            ["Audition", "Round 1", "Round 2", "Round 3"],
            competition.Rounds.OrderBy(r => r.SequenceNumber).Select(r => r.Name));
        Assert.Equal(RoundKind.Audition, competition.AuditionRound?.Kind);
        Assert.Equal(3, competition.MainRounds.Count);
    }

    [Fact]
    public void Rounds_an_organiser_added_by_hand_stand_in_for_the_main_rounds()
    {
        var competition = CompetitionAggregate.Create(
            Guid.NewGuid(), "Elocution", null, null, Now, Now.AddDays(7), Guid.NewGuid(), Now,
            format: new CompetitionFormat(2, 10, false, null, null));

        competition.CreateRound("Heats", Now);
        competition.MoveTo(CompetitionStatus.RegistrationOpen, Now);
        competition.MoveTo(CompetitionStatus.RegistrationClosed, Now);
        competition.MoveTo(CompetitionStatus.InProgress, Now);

        Assert.Equal("Heats", Assert.Single(competition.Rounds).Name);
    }

    [Fact]
    public void Turning_the_audition_off_forgets_its_date_and_finalists()
    {
        var competition = StartJudging(new CompetitionFormat(1, 10, false, new DateOnly(2026, 1, 10), 5));

        Assert.Null(competition.AuditionDate);
        Assert.Null(competition.FinalistCount);
        Assert.Null(competition.AuditionRound);
    }

    [Fact]
    public void A_judge_on_both_panels_judges_both_kinds_of_round()
    {
        var competition = StartJudging(new CompetitionFormat(1, 10, true, new DateOnly(2026, 1, 10), 5));
        var main = Guid.NewGuid();
        var audition = Guid.NewGuid();
        var both = Guid.NewGuid();

        competition.AssignJudge(main, true, Now, JudgePanel.Main);
        competition.AssignJudge(audition, true, Now, JudgePanel.Audition);
        competition.AssignJudge(both, true, Now, JudgePanel.Both);

        Assert.True(competition.CanJudge(main, RoundKind.Main));
        Assert.False(competition.CanJudge(main, RoundKind.Audition));
        Assert.True(competition.CanJudge(audition, RoundKind.Audition));
        Assert.False(competition.CanJudge(audition, RoundKind.Main));
        Assert.True(competition.CanJudge(both, RoundKind.Main));
        Assert.True(competition.CanJudge(both, RoundKind.Audition));
    }

    [Fact]
    public void Assigning_a_judge_again_on_another_panel_moves_them()
    {
        var competition = StartJudging(new CompetitionFormat(1, 10, true, new DateOnly(2026, 1, 10), 5));
        var judge = Guid.NewGuid();

        competition.AssignJudge(judge, true, Now, JudgePanel.Main);

        Assert.False(competition.AssignJudge(judge, true, Now, JudgePanel.Main));
        Assert.True(competition.AssignJudge(judge, true, Now, JudgePanel.Audition));
        Assert.Equal(JudgePanel.Audition, competition.FindJudge(judge)?.Panel);
        Assert.Single(competition.Judges);
    }
}
