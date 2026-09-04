using FluentAssertions;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Members;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

/// <summary>
/// Withdrawing the parental consent a child's record is held on (DPDP s.6(4)).
/// </summary>
/// <remarks>
/// The right existed on paper and was unreachable: the only way to withdraw was
/// to erase your own account, which is s.12 and takes your membership, your
/// household and everything you have written with it. Section 6(4) asks for
/// comparable ease, and giving was one tick.
/// </remarks>
public sealed class ParentalConsentWithdrawalTests
{
    private static readonly DateTimeOffset Given = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 6, 1, 9, 30, 0, TimeSpan.Zero);

    private static readonly Guid Parent = Guid.NewGuid();

    private static ChildProfile Child() =>
        ChildProfile.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Anaya Shah",
            new DateOnly(2015, 7, 14),
            Gender.Female,
            Parent,
            Given);

    [Fact]
    public void Withdrawing_records_who_and_when()
    {
        var child = Child();

        child.WithdrawParentalConsent(Parent, Later);

        child.ParentalConsent!.WithdrawnAt.Should().Be(Later);
        child.ParentalConsent.WithdrawnByMemberId.Should().Be(Parent);
        child.ParentalConsent.Stands.Should().BeFalse();
    }

    [Fact]
    public void And_keeps_what_was_agreed_to()
    {
        var child = Child();
        var version = child.ParentalConsent!.NoticeVersion;
        var attestation = child.ParentalConsent.Attestation;

        child.WithdrawParentalConsent(Parent, Later);

        // s.6(7) is about being able to demonstrate a consent. A withdrawal that
        // erased its own history could demonstrate nothing, including that the
        // consent had ever been properly obtained.
        child.ParentalConsent!.NoticeVersion.Should().Be(version);
        child.ParentalConsent.Attestation.Should().Be(attestation);
        child.ParentalConsent.GivenAt.Should().Be(Given);
        child.ParentalConsent.GivenByMemberId.Should().Be(Parent);
    }

    [Fact]
    public void The_record_is_de_identified_exactly_as_erasure_leaves_it()
    {
        var child = Child();

        child.WithdrawParentalConsent(Parent, Later);

        child.FullName.Should().Be("Erased child");
        child.PhotoImageId.Should().BeNull();
        child.Gender.Should().Be(Gender.Unspecified);

        // The year survives, shifted, because age decides conversion
        // eligibility and the row still has to behave.
        child.DateOfBirth.Should().Be(new DateOnly(2015, 1, 1));
    }

    [Fact]
    public void And_the_status_says_the_record_is_no_longer_held()
    {
        var child = Child();

        child.WithdrawParentalConsent(Parent, Later);

        // Without this the row stays on its household's screen forever, listed
        // as "Erased child" - which is what happened to every child whose
        // consent-giver erased their account.
        child.Status.Should().Be(ChildStatus.Withdrawn);
        child.IsEligibleForConversion(new DateOnly(2040, 1, 1)).Should().BeFalse();
    }

    [Fact]
    public void Erasing_leaves_the_same_status()
    {
        var child = Child();

        child.Erase();

        child.Status.Should().Be(ChildStatus.Withdrawn);
    }

    [Fact]
    public void Erasing_a_converted_child_does_not_move_their_status_back()
    {
        var child = Child();

        child.MarkConverted(Guid.NewGuid());
        child.Erase();

        // The row that remains is the historical link to an account that exists,
        // not a child record. Calling it Withdrawn would lose that.
        child.Status.Should().Be(ChildStatus.Converted);
    }

    [Fact]
    public void It_announces_the_withdrawal_without_announcing_the_child()
    {
        var child = Child();

        child.WithdrawParentalConsent(Parent, Later);

        var raised = child.DomainEvents
            .OfType<ParentalConsentWithdrawnDomainEvent>()
            .Single();

        raised.ChildProfileId.Should().Be(child.Id);
        raised.WithdrawnByMemberId.Should().Be(Parent);
        raised.OccurredAt.Should().Be(Later);

        // audit-notification-service stores payloads verbatim in an append-only
        // table. An event saying a child's data may no longer be held must not
        // be the copy of it that outlives everything else.
        //
        // **Asserted on the event's shape, not on its rendered text.** The first
        // version of this checked that ToString() did not contain "Anaya", which
        // could never have failed: WithdrawParentalConsent erases the record
        // before it raises, so by that line the name is already "Erased child".
        // A field carrying the name would have been added and the test would
        // have gone on passing - which fault injection is what showed.
        var carried = typeof(ParentalConsentWithdrawnDomainEvent)
            .GetProperties()
            .Where(p => p.Name != "EqualityContract" && p.Name != "Topic")
            .Select(p => p.PropertyType)
            .ToList();

        carried.Should().OnlyContain(
            t => t == typeof(Guid) || t == typeof(DateTimeOffset),
            "an event about a child's data may carry only ids and a time");
    }

    [Fact]
    public void Withdrawing_twice_keeps_the_first_moment()
    {
        var child = Child();
        var evenLater = Later.AddMonths(2);

        child.WithdrawParentalConsent(Parent, Later);
        child.WithdrawParentalConsent(Parent, evenLater);

        // The second timestamp would move the moment the record stopped being
        // justified, which is the one fact a regulator would ask for.
        child.ParentalConsent!.WithdrawnAt.Should().Be(Later);
    }
}
