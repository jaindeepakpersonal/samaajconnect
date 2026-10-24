using FluentAssertions;
using Sangam.Events.Domain.Events;
using Xunit;

namespace Sangam.Events.UnitTests;

/// <summary>
/// An event's schedule of programmes, and when an event counts as under way.
/// </summary>
public sealed class EventScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day1 = new(2026, 3, 31);

    private static SamaajEvent Published(DateTimeOffset startAt, DateTimeOffset? endAt)
    {
        var samaajEvent = SamaajEvent.Create(
            Guid.NewGuid(), "Paryushan", null, startAt, endAt, null, null,
            OrganizerType.Samaaj, null, Guid.NewGuid(), registrationEnabled: true, capacity: null, Now);

        samaajEvent.Publish(Now);
        samaajEvent.ClearDomainEvents();

        return samaajEvent;
    }

    private static SamaajEvent Upcoming() => Published(Now.AddDays(30), Now.AddDays(32));

    [Fact]
    public void The_schedule_reads_by_date_then_start_time_whatever_order_it_was_entered_in()
    {
        var samaajEvent = Upcoming();

        samaajEvent.AddScheduleItem(Day1.AddDays(1), "Pratikraman", new TimeOnly(18, 0), null, Now);
        samaajEvent.AddScheduleItem(Day1, "Pravachan", new TimeOnly(10, 0), new TimeOnly(11, 30), Now);
        samaajEvent.AddScheduleItem(Day1, "Prabhat Pheri", new TimeOnly(6, 0), new TimeOnly(7, 0), Now);

        samaajEvent.OrderedSchedule.Select(s => s.ProgramName)
            .Should().Equal("Prabhat Pheri", "Pravachan", "Pratikraman");
    }

    [Fact]
    public void Changing_the_schedule_is_an_edit_to_the_event()
    {
        var samaajEvent = Upcoming();

        var item = samaajEvent.AddScheduleItem(Day1, "Pravachan", new TimeOnly(10, 0), null, Now)!;
        samaajEvent.UpdateScheduleItem(item.Id, Day1, "Pravachan by Muni Shri", new TimeOnly(10, 30), null, Now)
            .Should().BeTrue();
        samaajEvent.RemoveScheduleItem(item.Id, Now).Should().BeTrue();

        samaajEvent.DomainEvents.Should().HaveCount(3).And.AllBeOfType<EventUpdatedDomainEvent>();
        samaajEvent.Schedule.Should().BeEmpty();
    }

    [Fact]
    public void A_programme_cannot_end_before_it_starts()
    {
        var act = () => Upcoming().AddScheduleItem(Day1, "Pravachan", new TimeOnly(11, 0), new TimeOnly(10, 0), Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_cancelled_event_takes_no_programmes()
    {
        var samaajEvent = Upcoming();
        var item = samaajEvent.AddScheduleItem(Day1, "Pravachan", new TimeOnly(10, 0), null, Now)!;

        samaajEvent.Cancel("Postponed to next month.", Now);

        samaajEvent.AddScheduleItem(Day1, "Bhakti", new TimeOnly(19, 0), null, Now).Should().BeNull();
        samaajEvent.UpdateScheduleItem(item.Id, Day1, "Changed", new TimeOnly(9, 0), null, Now).Should().BeFalse();
    }

    [Fact]
    public void An_unknown_programme_is_neither_updated_nor_removed()
    {
        var samaajEvent = Upcoming();

        samaajEvent.UpdateScheduleItem(Guid.NewGuid(), Day1, "X", new TimeOnly(9, 0), null, Now).Should().BeFalse();
        samaajEvent.RemoveScheduleItem(Guid.NewGuid(), Now).Should().BeFalse();
    }

    [Fact]
    public void An_event_is_happening_between_its_start_and_its_end()
    {
        var samaajEvent = Published(Now.AddHours(-1), Now.AddHours(2));

        samaajEvent.IsHappeningAt(Now).Should().BeTrue();
        samaajEvent.IsHappeningAt(Now.AddHours(-2)).Should().BeFalse("it has not started");
        samaajEvent.IsHappeningAt(Now.AddHours(3)).Should().BeFalse("it is over");
    }

    [Fact]
    public void An_event_with_no_end_is_taken_to_run_for_a_day()
    {
        var samaajEvent = Published(Now.AddHours(-6), null);

        samaajEvent.IsHappeningAt(Now).Should().BeTrue();
        samaajEvent.IsHappeningAt(Now.AddDays(1)).Should().BeFalse();
        samaajEvent.EndsBy.Should().Be(Now.AddHours(-6).Add(SamaajEvent.UntimedEventLength));
    }
}
