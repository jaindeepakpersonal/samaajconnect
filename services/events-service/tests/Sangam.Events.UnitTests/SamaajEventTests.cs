using FluentAssertions;
using Sangam.Events.Domain.Events;
using Xunit;

namespace Sangam.Events.UnitTests;

public sealed class SamaajEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StartAt = Now.AddDays(30);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OrganiserId = Guid.NewGuid();

    private static SamaajEvent Draft(int? capacity = null) =>
        SamaajEvent.Create(
            TenantId,
            "  Paryushan Lecture  ",
            "An evening lecture.",
            StartAt,
            StartAt.AddHours(2),
            "Community Hall",
            OrganizerType.Samaaj,
            organizerId: null,
            OrganiserId,
            registrationEnabled: true,
            capacity,
            Now);

    private static SamaajEvent Published(int? capacity = null)
    {
        var published = Draft(capacity);
        published.Publish(Now);
        published.ClearDomainEvents();

        return published;
    }

    // ---- Creation and publishing -----------------------------------------

    [Fact]
    public void An_event_starts_as_a_draft_and_announces_nothing()
    {
        // Writing an event down is not the same decision as telling the whole
        // Samaaj about it.
        var draft = Draft();

        draft.Status.Should().Be(EventStatus.Draft);
        draft.DomainEvents.Should().BeEmpty();
        draft.IsOpenForRegistration.Should().BeFalse();
    }

    [Fact]
    public void Creating_trims_the_title()
    {
        Draft().Title.Should().Be("Paryushan Lecture");
    }

    [Fact]
    public void Publishing_announces_it_once()
    {
        var draft = Draft(capacity: 200);

        draft.Publish(Now).Should().BeTrue();

        var raised = draft.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<EventPublishedDomainEvent>().Subject;

        raised.Capacity.Should().Be(200);
        raised.StartAt.Should().Be(StartAt);
    }

    [Fact]
    public void Publishing_twice_announces_nothing_the_second_time()
    {
        // Two organisers reaching for the same button is not a reason to tell
        // the Samaaj twice.
        var published = Published();

        published.Publish(Now).Should().BeFalse();
        published.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void The_published_event_carries_no_title_or_venue()
    {
        // audit-notification-service stores payloads verbatim; the free text is
        // the Samaaj's own copy and the ids are what another service acts on.
        var draft = Draft();
        draft.Publish(Now);

        var raised = draft.DomainEvents.Single();

        raised.GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(["Title", "Venue", "Description"]);
    }

    // ---- Registering ------------------------------------------------------

    [Fact]
    public void A_member_registers_and_takes_a_place()
    {
        var published = Published(capacity: 2);

        var registration = published.Register(Guid.NewGuid(), Now);

        registration!.Status.Should().Be(RegistrationStatus.Registered);
        published.RegisteredCount.Should().Be(1);
    }

    [Fact]
    public void A_draft_takes_no_registrations()
    {
        Draft().Register(Guid.NewGuid(), Now).Should().BeNull();
    }

    [Fact]
    public void An_event_that_has_started_takes_no_registrations()
    {
        var published = Published();

        published.Register(Guid.NewGuid(), StartAt.AddMinutes(1)).Should().BeNull();
    }

    [Fact]
    public void Registering_twice_is_a_no_op()
    {
        var published = Published();
        var member = Guid.NewGuid();

        published.Register(member, Now).Should().NotBeNull();

        published.Register(member, Now).Should().BeNull();
        published.RegisteredCount.Should().Be(1);
    }

    // ---- Capacity and the waitlist ---------------------------------------

    [Fact]
    public void Past_capacity_a_member_is_waitlisted_rather_than_refused()
    {
        // The wireframe's "Full — Waitlist" pill and its "Join Waitlist"
        // button. Which one a member gets depends on the room at that moment,
        // not on which button they pressed.
        var published = Published(capacity: 1);
        published.Register(Guid.NewGuid(), Now);

        var second = published.Register(Guid.NewGuid(), Now);

        second!.Status.Should().Be(RegistrationStatus.Waitlisted);
        published.RegisteredCount.Should().Be(1);
        published.WaitlistedCount.Should().Be(1);
    }

    [Fact]
    public void An_event_with_no_capacity_never_fills_up()
    {
        // Null means no limit, which is a different thing from a limit of zero.
        var published = Published(capacity: null);

        for (var i = 0; i < 50; i++)
        {
            published.Register(Guid.NewGuid(), Now);
        }

        published.IsFull.Should().BeFalse();
        published.WaitlistedCount.Should().Be(0);
    }

    [Fact]
    public void Filling_up_is_announced_once_as_the_last_place_goes()
    {
        var published = Published(capacity: 2);

        published.Register(Guid.NewGuid(), Now);
        published.DomainEvents.OfType<EventCapacityReachedDomainEvent>().Should().BeEmpty();

        published.Register(Guid.NewGuid(), Now);
        published.DomainEvents.OfType<EventCapacityReachedDomainEvent>().Should().ContainSingle();

        // A third member joining the waitlist is not the event filling up
        // again.
        published.ClearDomainEvents();
        published.Register(Guid.NewGuid(), Now);
        published.DomainEvents.OfType<EventCapacityReachedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Giving_up_a_place_promotes_whoever_waited_longest()
    {
        // Without this the waitlist is a list nobody ever comes off, which is
        // worse than not offering one.
        var published = Published(capacity: 1);
        var holder = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        published.Register(holder, Now);
        published.Register(first, Now.AddMinutes(1));
        published.Register(second, Now.AddMinutes(2));

        var outcome = published.CancelRegistration(holder, Now.AddMinutes(3));

        outcome.PromotedMemberId.Should().Be(first);
        published.FindRegistration(first)!.Status.Should().Be(RegistrationStatus.Registered);
        published.FindRegistration(second)!.Status.Should().Be(RegistrationStatus.Waitlisted);
    }

    [Fact]
    public void A_promoted_member_keeps_their_place_in_the_queue()
    {
        // Refreshing the timestamp on promotion would put them behind people
        // who joined the waitlist after them.
        var published = Published(capacity: 1);
        var holder = Guid.NewGuid();
        var first = Guid.NewGuid();

        published.Register(holder, Now);
        published.Register(first, Now.AddMinutes(1));

        published.CancelRegistration(holder, Now.AddMinutes(5));

        published.FindRegistration(first)!.RegisteredAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Leaving_the_waitlist_promotes_nobody()
    {
        var published = Published(capacity: 1);
        published.Register(Guid.NewGuid(), Now);

        var waiting = Guid.NewGuid();
        published.Register(waiting, Now.AddMinutes(1));

        var outcome = published.CancelRegistration(waiting, Now.AddMinutes(2));

        outcome.Cancelled.Should().BeTrue();
        outcome.PromotedMemberId.Should().BeNull();
    }

    [Fact]
    public void Promotion_is_announced_so_the_member_can_be_told()
    {
        // A member who is told nothing has effectively not been promoted.
        var published = Published(capacity: 1);
        var holder = Guid.NewGuid();
        var waiting = Guid.NewGuid();

        published.Register(holder, Now);
        published.Register(waiting, Now.AddMinutes(1));
        published.ClearDomainEvents();

        published.CancelRegistration(holder, Now.AddMinutes(2));

        published.DomainEvents.OfType<EventWaitlistPromotedDomainEvent>()
            .Should().ContainSingle().Which.MemberId.Should().Be(waiting);
    }

    [Fact]
    public void Coming_back_after_cancelling_goes_to_the_back_of_the_queue()
    {
        // Otherwise cancelling would be free and the waitlist would never move.
        var published = Published(capacity: 1);
        var holder = Guid.NewGuid();
        var member = Guid.NewGuid();

        published.Register(member, Now);
        published.CancelRegistration(member, Now.AddMinutes(1));

        // Somebody else takes the place while they are away.
        published.Register(holder, Now.AddMinutes(2));

        var again = published.Register(member, Now.AddMinutes(3));

        again!.Status.Should().Be(RegistrationStatus.Waitlisted);
        published.FindRegistration(member)!.RegisteredAt.Should().Be(Now.AddMinutes(3));
    }

    [Fact]
    public void Cancelling_something_you_never_had_does_nothing()
    {
        Published().CancelRegistration(Guid.NewGuid(), Now).Cancelled.Should().BeFalse();
    }

    // ---- Cancelling the event --------------------------------------------

    [Fact]
    public void Cancelling_keeps_the_registrations()
    {
        // People need to be told, and an attendee list that vanished is one
        // nobody can notify.
        var published = Published();
        published.Register(Guid.NewGuid(), Now);

        published.Cancel("The hall is unavailable.", Now.AddDays(1)).Should().BeTrue();

        published.Status.Should().Be(EventStatus.Cancelled);
        published.Registrations.Should().NotBeEmpty();
    }

    [Fact]
    public void Cancelling_announces_how_many_people_were_expecting_it()
    {
        var published = Published(capacity: 1);
        published.Register(Guid.NewGuid(), Now);
        published.Register(Guid.NewGuid(), Now.AddMinutes(1));
        published.ClearDomainEvents();

        published.Cancel("The hall is unavailable.", Now.AddDays(1));

        var raised = published.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<EventCancelledDomainEvent>().Subject;

        // Everyone who was expecting it, waitlist included: they were planning
        // around it too.
        raised.AffectedRegistrations.Should().Be(2);
    }

    [Fact]
    public void Cancelling_twice_announces_nothing_the_second_time()
    {
        var published = Published();
        published.Cancel("Off.", Now);
        published.ClearDomainEvents();

        published.Cancel("Off again.", Now).Should().BeFalse();
        published.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_cancelled_event_takes_no_registrations()
    {
        var published = Published();
        published.Cancel("Off.", Now);

        published.Register(Guid.NewGuid(), Now).Should().BeNull();
    }
}
