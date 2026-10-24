using FluentAssertions;
using Sangam.Pathshala.Domain.Donations;
using Xunit;

namespace Sangam.Pathshala.UnitTests;

/// <summary>
/// A donation's two steps - announced, then paid - and what deleting keeps.
/// </summary>
public sealed class PathshalaDonationTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 3, 1);

    private static PathshalaDonation APledge() =>
        PathshalaDonation.Pledge(
            Guid.NewGuid(), Guid.NewGuid(), "Smt. Kavita Jain", null, 210_000, Day, "Books", Guid.NewGuid(), Now);

    private static PathshalaDonation APaidDonation() =>
        PathshalaDonation.Record(
            Guid.NewGuid(), Guid.NewGuid(), 1, "Anonymous", null, 110_000, Day, DonationMode.Cash, null, Guid.NewGuid(), Now);

    [Fact]
    public void An_announcement_has_no_receipt_mode_or_paid_date()
    {
        var pledge = APledge();

        pledge.Status.Should().Be(DonationStatus.Pledged);
        pledge.ReceiptNumber.Should().BeNull("a receipt means money received");
        pledge.Mode.Should().BeNull();
        pledge.PaidOn.Should().BeNull();
        pledge.DomainEvents.OfType<DonationRecordedDomainEvent>().Single().Pledged.Should().BeTrue();
    }

    [Fact]
    public void Money_recorded_as_paid_is_paid_on_the_day_it_was_given()
    {
        var paid = APaidDonation();

        paid.Status.Should().Be(DonationStatus.Paid);
        paid.PaidOn.Should().Be(Day);
    }

    [Fact]
    public void Marking_an_announcement_paid_issues_its_receipt_once()
    {
        var pledge = APledge();

        pledge.MarkPaid(4, DonationMode.Upi, Day.AddDays(5), Guid.NewGuid(), Now).Should().BeTrue();

        pledge.Status.Should().Be(DonationStatus.Paid);
        pledge.ReceiptNumber.Should().Be(4);
        pledge.Mode.Should().Be(DonationMode.Upi);
        pledge.PaidOn.Should().Be(Day.AddDays(5));

        pledge.MarkPaid(5, DonationMode.Cash, Day, Guid.NewGuid(), Now).Should().BeFalse("paid once");
        pledge.ReceiptNumber.Should().Be(4);
    }

    [Fact]
    public void Correcting_an_announcement_leaves_it_an_announcement()
    {
        var pledge = APledge();

        pledge.Correct("Smt. Kavita Jain", 250_000, Day, DonationMode.Cash, Day, null, Guid.NewGuid(), Now)
            .Should().BeTrue();

        pledge.Status.Should().Be(DonationStatus.Pledged);
        pledge.Mode.Should().BeNull("only marking it paid gives it a mode");
        pledge.Amount.Should().Be(250_000);
    }

    [Fact]
    public void A_paid_entry_cannot_be_corrected_without_how_it_was_paid()
    {
        var paid = APaidDonation();

        var act = () => paid.Correct("Anonymous", 110_000, Day, null, Day, null, Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Voiding_an_entry_with_a_receipt_needs_a_reason()
    {
        var act = () => APaidDonation().Void(" ", Guid.NewGuid(), Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_announcement_is_erased_never_voided()
    {
        var pledge = APledge();

        var voiding = () => pledge.Void("Will not be paid", Guid.NewGuid(), Now);
        voiding.Should().Throw<InvalidOperationException>();

        pledge.Erase(Guid.NewGuid(), Now).Should().BeTrue();
        pledge.DomainEvents.OfType<DonationErasedDomainEvent>().Should().ContainSingle();
    }

    [Fact]
    public void An_entry_with_a_receipt_is_never_erased()
    {
        var paid = APaidDonation();

        paid.Erase(Guid.NewGuid(), Now).Should().BeFalse("its receipt number is on a slip");
        paid.DomainEvents.OfType<DonationErasedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void A_voided_entry_is_neither_corrected_nor_voided_again()
    {
        var paid = APaidDonation();
        paid.Void("Entered twice", Guid.NewGuid(), Now);

        paid.Correct("X", 1, Day, DonationMode.Cash, Day, null, Guid.NewGuid(), Now).Should().BeFalse();
        paid.Void("Again", Guid.NewGuid(), Now).Should().BeFalse();
    }
}
