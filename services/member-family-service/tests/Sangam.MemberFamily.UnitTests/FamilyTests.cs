using FluentAssertions;
using Sangam.MemberFamily.Domain.Families;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

public sealed class FamilyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Head = Guid.NewGuid();

    private static Family NewFamily() => Family.Create(TenantId, Head, "ABCD2345", Now);

    [Fact]
    public void The_head_is_an_active_member_of_their_own_family_from_the_start()
    {
        var family = NewFamily();

        var member = family.Members.Should().ContainSingle().Subject;

        member.MemberProfileId.Should().Be(Head);
        member.Status.Should().Be(FamilyMemberStatus.Active);
        family.IsHead(Head).Should().BeTrue();
    }

    [Fact]
    public void Creating_a_family_raises_an_event()
    {
        NewFamily().DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<FamilyCreatedDomainEvent>();
    }

    [Fact]
    public void A_generated_code_avoids_the_characters_people_misread()
    {
        // 0/O and 1/I/L are what go wrong when a code is read aloud between
        // relatives, which is how these actually travel.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Family.GenerateCode().Should().MatchRegex("^[A-HJ-NP-Z2-9]{8}$");
        }
    }

    [Fact]
    public void A_join_request_starts_pending()
    {
        var family = NewFamily();
        var joiner = Guid.NewGuid();

        var request = family.RequestJoin(joiner, Relationship.Spouse, Now);

        request.Should().NotBeNull();
        request!.Status.Should().Be(FamilyMemberStatus.PendingJoinRequest);
        family.Members.Should().HaveCount(2);
    }

    [Fact]
    public void Asking_twice_does_not_give_the_head_two_requests_to_decide()
    {
        var family = NewFamily();
        var joiner = Guid.NewGuid();

        family.RequestJoin(joiner, Relationship.Spouse, Now);
        var second = family.RequestJoin(joiner, Relationship.Spouse, Now.AddMinutes(1));

        second.Should().BeNull();
        family.Members.Should().HaveCount(2);
    }

    [Fact]
    public void An_existing_member_cannot_re_request()
    {
        var family = NewFamily();

        family.RequestJoin(Head, Relationship.Other, Now).Should().BeNull();
    }

    [Fact]
    public void Someone_previously_rejected_may_ask_again()
    {
        var family = NewFamily();
        var joiner = Guid.NewGuid();

        var request = family.RequestJoin(joiner, Relationship.Sibling, Now)!;
        family.DecideJoinRequest(request.Id, accepted: false, Head, Now);

        // Circumstances and minds both change.
        var second = family.RequestJoin(joiner, Relationship.Sibling, Now.AddDays(30));

        second.Should().NotBeNull();
        second!.Status.Should().Be(FamilyMemberStatus.PendingJoinRequest);
        family.Members.Should().HaveCount(2);
    }

    [Fact]
    public void Accepting_a_request_makes_the_member_active_and_records_who_decided()
    {
        var family = NewFamily();
        var request = family.RequestJoin(Guid.NewGuid(), Relationship.Spouse, Now)!;

        family.DecideJoinRequest(request.Id, accepted: true, Head, Now.AddHours(1)).Should().BeTrue();

        request.Status.Should().Be(FamilyMemberStatus.Active);
        request.DecidedBy.Should().Be(Head);
        request.DecidedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Rejecting_a_request_records_it_rather_than_deleting_it()
    {
        var family = NewFamily();
        var request = family.RequestJoin(Guid.NewGuid(), Relationship.Spouse, Now)!;

        family.DecideJoinRequest(request.Id, accepted: false, Head, Now);

        request.Status.Should().Be(FamilyMemberStatus.Rejected);
    }

    [Fact]
    public void Deciding_the_same_request_twice_is_refused()
    {
        var family = NewFamily();
        var request = family.RequestJoin(Guid.NewGuid(), Relationship.Spouse, Now)!;

        family.DecideJoinRequest(request.Id, accepted: true, Head, Now).Should().BeTrue();
        family.DecideJoinRequest(request.Id, accepted: false, Head, Now).Should().BeFalse();
    }

    [Fact]
    public void Deciding_an_unknown_request_is_refused()
    {
        NewFamily().DecideJoinRequest(Guid.NewGuid(), accepted: true, Head, Now).Should().BeFalse();
    }

    [Fact]
    public void A_family_code_is_stored_uppercased()
    {
        Family.Create(TenantId, Head, " abcd2345 ", Now).FamilyCode.Should().Be("ABCD2345");
    }
}
