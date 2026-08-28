using FluentAssertions;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Members;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

public sealed class ChildProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 6, 1);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid FamilyId = Guid.NewGuid();

    private static ChildProfile ChildBornOn(DateOnly dateOfBirth) =>
        ChildProfile.Create(TenantId, FamilyId, "Aarav Jain", dateOfBirth, Gender.Male, null, Now);

    [Fact]
    public void A_new_child_is_a_minor_with_no_account_of_their_own()
    {
        var child = ChildBornOn(new DateOnly(2015, 3, 2));

        child.Status.Should().Be(ChildStatus.Minor);
        child.ConvertedMemberId.Should().BeNull();
    }

    [Theory]
    [InlineData("2008-05-14", 18)]
    [InlineData("2008-06-01", 18)]
    [InlineData("2008-06-02", 17)]
    [InlineData("2026-06-01", 0)]
    public void Age_is_counted_from_the_birthday_not_the_year(string dateOfBirth, int expected)
    {
        ChildBornOn(DateOnly.Parse(dateOfBirth)).AgeOn(Today).Should().Be(expected);
    }

    [Fact]
    public void A_child_becomes_eligible_on_their_eighteenth_birthday_and_not_before()
    {
        // Derived from the date of birth rather than stored, so there is no
        // nightly job that can fail to run and leave this silently wrong.
        ChildBornOn(new DateOnly(2008, 6, 2)).IsEligibleForConversion(Today).Should().BeFalse();
        ChildBornOn(new DateOnly(2008, 6, 1)).IsEligibleForConversion(Today).Should().BeTrue();
    }

    [Fact]
    public void An_already_converted_child_is_not_eligible_again()
    {
        var child = ChildBornOn(new DateOnly(2000, 1, 1));

        child.MarkConverted(Guid.NewGuid());

        child.IsEligibleForConversion(Today).Should().BeFalse();
    }

    [Fact]
    public void Marking_converted_records_which_member_account_it_became()
    {
        var child = ChildBornOn(new DateOnly(2000, 1, 1));
        var memberId = Guid.NewGuid();

        child.MarkConverted(memberId);

        child.Status.Should().Be(ChildStatus.Converted);
        child.ConvertedMemberId.Should().Be(memberId);
    }
}

public sealed class ChildConversionRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ChildId = Guid.NewGuid();
    private static readonly Guid Head = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static ChildConversionRequest NewRequest() =>
        ChildConversionRequest.Raise(TenantId, ChildId, Head, " Aarav@Example.COM ", Now);

    [Fact]
    public void A_request_starts_pending_and_normalises_the_identifier()
    {
        var request = NewRequest();

        request.Status.Should().Be(ConversionStatus.Pending);
        request.MobileOrEmail.Should().Be("aarav@example.com");
        request.RequestedByMemberId.Should().Be(Head);
    }

    [Fact]
    public void Approving_records_the_decision_and_announces_it()
    {
        var request = NewRequest();

        request.Approve(Admin, "Verified in person", Now.AddDays(1), "Aarav Jain").Should().BeTrue();

        request.Status.Should().Be(ConversionStatus.Approved);
        request.DecidedBy.Should().Be(Admin);
        request.DecisionNote.Should().Be("Verified in person");

        var raised = request.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ChildConversionApprovedDomainEvent>().Subject;

        raised.ChildProfileId.Should().Be(ChildId);
        raised.MobileOrEmail.Should().Be("aarav@example.com");
        raised.Topic.Should().Be("members.child-conversion.approved.v1");
    }

    [Fact]
    public void The_approval_event_carries_no_credential()
    {
        var request = NewRequest();
        request.Approve(Admin, null, Now, "Aarav Jain");

        var raised = (ChildConversionApprovedDomainEvent)request.DomainEvents.Single();

        // audit-notification-service records every payload verbatim into an
        // append-only table, so anything secret here would be unredactable.
        var properties = raised.GetType().GetProperties().Select(p => p.Name.ToLowerInvariant());

        properties.Should().NotContain(name => name.Contains("password"));
        properties.Should().NotContain(name => name.Contains("hash"));
        properties.Should().NotContain(name => name.Contains("secret"));
    }

    [Fact]
    public void Rejecting_records_the_decision_without_announcing_anything()
    {
        var request = NewRequest();

        request.Reject(Admin, "Not the right person", Now).Should().BeTrue();

        request.Status.Should().Be(ConversionStatus.Rejected);

        // Nothing downstream should act on a refusal.
        request.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_request_can_only_be_decided_once()
    {
        var request = NewRequest();

        request.Approve(Admin, null, Now, "Aarav Jain").Should().BeTrue();
        request.Approve(Admin, null, Now, "Aarav Jain").Should().BeFalse();
        request.Reject(Admin, null, Now).Should().BeFalse();
    }

    [Fact]
    public void A_rejected_request_cannot_later_be_approved()
    {
        var request = NewRequest();

        request.Reject(Admin, null, Now).Should().BeTrue();
        request.Approve(Admin, null, Now, "Aarav Jain").Should().BeFalse();
        request.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_blank_decision_note_is_stored_as_nothing_rather_than_whitespace()
    {
        var request = NewRequest();

        request.Approve(Admin, "   ", Now, "Aarav Jain");

        request.DecisionNote.Should().BeNull();
    }
}
