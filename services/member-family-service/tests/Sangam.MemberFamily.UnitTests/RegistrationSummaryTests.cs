using FluentAssertions;
using NSubstitute;
using Sangam.MemberFamily.Application.Abstractions;
using Sangam.MemberFamily.Application.Members.Queries.GetRegistrationSummary;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

public sealed class RegistrationSummaryTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IMemberProfileRepository _profiles = Substitute.For<IMemberProfileRepository>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();
    private readonly GetRegistrationSummaryQueryHandler _handler;

    public RegistrationSummaryTests()
    {
        _clock.UtcNow.Returns(Now);
        _handler = new GetRegistrationSummaryQueryHandler(_profiles, _clock);
    }

    [Fact]
    public async Task Reports_the_current_and_previous_thirty_day_counts()
    {
        var currentStart = Now - TimeSpan.FromDays(30);
        var previousStart = currentStart - TimeSpan.FromDays(30);

        _profiles.CountCreatedBetweenAsync(currentStart, Now, Arg.Any<CancellationToken>())
            .Returns(18);
        _profiles.CountCreatedBetweenAsync(previousStart, currentStart, Arg.Any<CancellationToken>())
            .Returns(15);

        var result = await _handler.Handle(new GetRegistrationSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.CurrentPeriodCount.Should().Be(18);
        result.Value.PreviousPeriodCount.Should().Be(15);
        result.Value.GrowthPercentage.Should().BeApproximately(20.0, 0.01);
    }

    [Fact]
    public async Task Reports_negative_growth_when_registrations_slowed()
    {
        _profiles.CountCreatedBetweenAsync(
                Arg.Any<DateTimeOffset>(), Now, Arg.Any<CancellationToken>())
            .Returns(5);
        _profiles.CountCreatedBetweenAsync(
                Arg.Any<DateTimeOffset>(), Arg.Is<DateTimeOffset>(d => d != Now),
                Arg.Any<CancellationToken>())
            .Returns(10);

        var result = await _handler.Handle(new GetRegistrationSummaryQuery(), CancellationToken.None);

        result.Value.GrowthPercentage.Should().BeApproximately(-50.0, 0.01);
    }

    [Fact]
    public async Task Reports_no_growth_percentage_when_the_previous_period_counted_nothing()
    {
        // A percentage against zero is not a number, and zero would misreport
        // growth from nothing as no growth at all.
        _profiles.CountCreatedBetweenAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await _handler.Handle(new GetRegistrationSummaryQuery(), CancellationToken.None);

        result.Value.CurrentPeriodCount.Should().Be(0);
        result.Value.PreviousPeriodCount.Should().Be(0);
        result.Value.GrowthPercentage.Should().BeNull();
    }
}
