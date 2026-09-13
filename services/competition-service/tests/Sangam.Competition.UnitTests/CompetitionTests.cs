using Sangam.Competition.Domain.Competitions;
using Xunit;
using CompetitionAggregate = Sangam.Competition.Domain.Competitions.Competition;

namespace Sangam.Competition.UnitTests;

public sealed class CompetitionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CompetitionAggregate CreateOpenForRegistration()
    {
        var competition = CompetitionAggregate.Create(
            TenantId, "Rangoli Contest", "Annual rangoli making contest", null,
            Now.AddHours(-1), Now.AddDays(7), Guid.NewGuid(), Now.AddDays(-1));

        competition.MoveTo(CompetitionStatus.RegistrationOpen, Now);

        return competition;
    }

    [Fact]
    public void MoveTo_follows_the_forward_sequence_only()
    {
        var competition = CompetitionAggregate.Create(
            TenantId, "Rangoli Contest", null, null, Now, Now.AddDays(7), Guid.NewGuid(), Now);

        Assert.False(competition.MoveTo(CompetitionStatus.RegistrationClosed, Now));
        Assert.False(competition.MoveTo(CompetitionStatus.InProgress, Now));
        Assert.False(competition.MoveTo(CompetitionStatus.Closed, Now));

        Assert.True(competition.MoveTo(CompetitionStatus.RegistrationOpen, Now));
        Assert.False(competition.MoveTo(CompetitionStatus.RegistrationOpen, Now));
        Assert.True(competition.MoveTo(CompetitionStatus.RegistrationClosed, Now));
        Assert.True(competition.MoveTo(CompetitionStatus.InProgress, Now));
        Assert.True(competition.MoveTo(CompetitionStatus.Closed, Now));
        Assert.False(competition.MoveTo(CompetitionStatus.RegistrationOpen, Now));
    }

    [Fact]
    public void AcceptsRegistrations_requires_both_the_status_and_the_window()
    {
        var competition = CompetitionAggregate.Create(
            TenantId, "Rangoli Contest", null, null, Now.AddDays(1), Now.AddDays(7), Guid.NewGuid(), Now);

        competition.MoveTo(CompetitionStatus.RegistrationOpen, Now);

        // Status says open, but the window has not started yet.
        Assert.False(competition.AcceptsRegistrations(Now));
        Assert.True(competition.AcceptsRegistrations(Now.AddDays(1)));
        Assert.False(competition.AcceptsRegistrations(Now.AddDays(8)));
    }

    [Fact]
    public void Register_refuses_when_registration_is_not_open()
    {
        var competition = CompetitionAggregate.Create(
            TenantId, "Rangoli Contest", null, null, Now, Now.AddDays(7), Guid.NewGuid(), Now);

        Assert.Null(competition.Register(Guid.NewGuid(), Now));
    }

    [Fact]
    public void Register_is_one_per_member_and_withdraw_then_reregister_reactivates()
    {
        var competition = CreateOpenForRegistration();
        var memberId = Guid.NewGuid();

        var first = competition.Register(memberId, Now);
        Assert.NotNull(first);
        Assert.True(competition.IsRegistered(memberId));

        // Registering again while already registered is a no-op, not an error.
        var second = competition.Register(memberId, Now);
        Assert.Equal(first!.Id, second!.Id);

        Assert.True(competition.Withdraw(memberId, Now));
        Assert.False(competition.IsRegistered(memberId));

        // Withdrawing twice is refused.
        Assert.False(competition.Withdraw(memberId, Now));

        var reactivated = competition.Register(memberId, Now);
        Assert.Equal(first.Id, reactivated!.Id);
        Assert.True(competition.IsRegistered(memberId));
    }

    [Fact]
    public void AssignJudge_refuses_a_duplicate_assignment_and_a_removal_of_nobody()
    {
        var competition = CreateOpenForRegistration();
        var judgeId = Guid.NewGuid();

        Assert.True(competition.AssignJudge(judgeId, assign: true, Now));
        Assert.True(competition.IsJudge(judgeId));

        Assert.False(competition.AssignJudge(judgeId, assign: true, Now));
        Assert.True(competition.AssignJudge(judgeId, assign: false, Now));
        Assert.False(competition.IsJudge(judgeId));
        Assert.False(competition.AssignJudge(judgeId, assign: false, Now));
    }

    [Fact]
    public void CreateRound_refuses_once_the_competition_is_closed()
    {
        var competition = CompetitionAggregate.Create(
            TenantId, "Rangoli Contest", null, null, Now, Now.AddDays(7), Guid.NewGuid(), Now);

        competition.MoveTo(CompetitionStatus.RegistrationOpen, Now);
        competition.MoveTo(CompetitionStatus.RegistrationClosed, Now);
        competition.MoveTo(CompetitionStatus.InProgress, Now);
        competition.MoveTo(CompetitionStatus.Closed, Now);

        Assert.Null(competition.CreateRound("Final", Now));
    }

    [Fact]
    public void CreateRound_assigns_sequence_numbers_in_order()
    {
        var competition = CreateOpenForRegistration();

        var first = competition.CreateRound("Round 1", Now);
        var second = competition.CreateRound("Round 2", Now);

        Assert.Equal(1, first!.SequenceNumber);
        Assert.Equal(2, second!.SequenceNumber);
    }

    [Fact]
    public void CloseRound_refuses_an_unknown_round_and_a_second_close()
    {
        var competition = CreateOpenForRegistration();
        var round = competition.CreateRound("Round 1", Now)!;

        Assert.False(competition.CloseRound(Guid.NewGuid(), Now));
        Assert.True(competition.CloseRound(round.Id, Now));
        Assert.False(competition.CloseRound(round.Id, Now));
    }
}
