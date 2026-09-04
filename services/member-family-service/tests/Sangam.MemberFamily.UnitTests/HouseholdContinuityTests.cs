using FluentAssertions;
using Sangam.MemberFamily.Domain.Families;
using Xunit;

namespace Sangam.MemberFamily.UnitTests;

/// <summary>
/// The two ways a household could become a dead end, and what stops them.
/// </summary>
/// <remarks>
/// Both are about somebody being stuck through no act of their own: a member
/// whose request nobody decides, and a household whose head erases. Neither
/// needed a bug to reach - the first is what happens when a head is simply
/// slow.
/// </remarks>
public sealed class HouseholdContinuityTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static Family Household(Guid headId) =>
        Family.Create(TenantId, headId, Family.GenerateCode(), Start);

    private static Guid Join(Family family, Family household, DateTimeOffset at, bool accept = true)
    {
        var memberId = Guid.NewGuid();
        var request = family.RequestJoin(memberId, Relationship.Sibling, at)!;

        if (accept)
        {
            family.DecideJoinRequest(request.Id, accepted: true, household.FamilyHeadMemberId, at);
        }

        return memberId;
    }

    // ---- Withdrawing a request ---------------------------------------------

    /// <summary>
    /// The escape hatch that did not exist. A pending request counts as
    /// belonging to a household, so a member waiting on one could not join
    /// anywhere else or create their own - and nothing could cancel it.
    /// </summary>
    [Fact]
    public void A_member_can_take_back_a_request_nobody_has_decided()
    {
        var family = Household(Guid.NewGuid());
        var asker = Guid.NewGuid();

        family.RequestJoin(asker, Relationship.Sibling, Start);

        family.WithdrawJoinRequest(asker).Should().Be(Family.WithdrawOutcome.Withdrawn);
        family.FindMember(asker).Should().BeNull();
    }

    [Fact]
    public void Withdrawing_when_there_is_nothing_pending_changes_nothing()
    {
        var family = Household(Guid.NewGuid());

        family.WithdrawJoinRequest(Guid.NewGuid())
            .Should().Be(Family.WithdrawOutcome.NothingPending);
    }

    [Fact]
    public void Withdrawing_twice_looks_exactly_like_withdrawing_once()
    {
        var family = Household(Guid.NewGuid());
        var asker = Guid.NewGuid();
        family.RequestJoin(asker, Relationship.Sibling, Start);

        family.WithdrawJoinRequest(asker).Should().Be(Family.WithdrawOutcome.Withdrawn);
        family.WithdrawJoinRequest(asker).Should().Be(Family.WithdrawOutcome.NothingPending);
    }

    /// <summary>
    /// The race that matters. The head accepted while the member was deciding
    /// to withdraw, so this is a membership now - and a call that quietly
    /// succeeded would remove somebody from a household they had just joined
    /// while telling them they had cancelled a request.
    /// </summary>
    [Fact]
    public void An_accepted_request_is_not_something_withdrawing_can_undo()
    {
        var headId = Guid.NewGuid();
        var family = Household(headId);
        var asker = Guid.NewGuid();

        var request = family.RequestJoin(asker, Relationship.Sibling, Start)!;
        family.DecideJoinRequest(request.Id, accepted: true, headId, Start);

        family.WithdrawJoinRequest(asker).Should().Be(Family.WithdrawOutcome.AlreadyAccepted);
        family.FindMember(asker).Should().NotBeNull();
    }

    /// <summary>
    /// A rejected request already lets a member ask elsewhere, so there is
    /// nothing standing to take back.
    /// </summary>
    [Fact]
    public void A_rejected_request_is_nothing_left_to_withdraw()
    {
        var headId = Guid.NewGuid();
        var family = Household(headId);
        var asker = Guid.NewGuid();

        var request = family.RequestJoin(asker, Relationship.Sibling, Start)!;
        family.DecideJoinRequest(request.Id, accepted: false, headId, Start);

        family.WithdrawJoinRequest(asker).Should().Be(Family.WithdrawOutcome.NothingPending);
    }

    // ---- Succession ---------------------------------------------------------

    /// <summary>
    /// Four things stopped working at once when a head erased: deciding a join
    /// request, adding a child, starting a conversion, and seeing the family
    /// code. This is what stops the household reaching that state at all.
    /// </summary>
    [Fact]
    public void Headship_passes_to_the_longest_standing_member_when_the_head_goes()
    {
        var headId = Guid.NewGuid();
        var family = Household(headId);

        var older = Join(family, family, Start.AddDays(1));
        var newer = Join(family, family, Start.AddDays(30));

        family.RemoveMember(headId);

        family.SucceedHeadAfterRemoval(headId).Should().Be(older);
        family.IsHead(older).Should().BeTrue();
        family.IsHead(newer).Should().BeFalse();
    }

    /// <summary>
    /// Longest-standing is the earliest to have *joined*, not the earliest to
    /// have asked. A request accepted last week does not outrank a member of
    /// ten years because they filled a form first.
    /// </summary>
    [Fact]
    public void Succession_goes_by_when_somebody_joined_not_when_they_asked()
    {
        var headId = Guid.NewGuid();
        var family = Household(headId);

        // Asked first, accepted last.
        var slowToBeAccepted = Guid.NewGuid();
        var slowRequest = family.RequestJoin(slowToBeAccepted, Relationship.Sibling, Start.AddDays(1))!;

        var acceptedSooner = Join(family, family, Start.AddDays(10));

        family.DecideJoinRequest(slowRequest.Id, accepted: true, headId, Start.AddDays(90));

        family.RemoveMember(headId);

        family.SucceedHeadAfterRemoval(headId).Should().Be(acceptedSooner);
    }

    [Fact]
    public void A_pending_request_never_inherits_a_household()
    {
        var headId = Guid.NewGuid();
        var family = Household(headId);

        var waiting = Guid.NewGuid();
        family.RequestJoin(waiting, Relationship.Sibling, Start.AddDays(1));

        family.RemoveMember(headId);

        // Nobody active is left, so there is no successor - and somebody who
        // has only asked to join is not somebody to hand a household to.
        family.SucceedHeadAfterRemoval(headId).Should().BeNull();
        family.IsHead(waiting).Should().BeFalse();
    }

    [Fact]
    public void A_household_with_nobody_left_keeps_the_head_it_had()
    {
        var headId = Guid.NewGuid();
        var family = Household(headId);

        family.RemoveMember(headId);

        family.SucceedHeadAfterRemoval(headId).Should().BeNull();
        family.FamilyHeadMemberId.Should().Be(headId);
    }

    /// <summary>
    /// Only the head's departure triggers succession. An ordinary member
    /// leaving must not move headship.
    /// </summary>
    [Fact]
    public void An_ordinary_member_leaving_does_not_change_who_heads_the_household()
    {
        var headId = Guid.NewGuid();
        var family = Household(headId);

        var other = Join(family, family, Start.AddDays(1));

        family.RemoveMember(other);

        family.SucceedHeadAfterRemoval(other).Should().BeNull();
        family.IsHead(headId).Should().BeTrue();
    }

  /// <summary>
  /// A head who leaves hands the household on, exactly as one who erases does.
  /// </summary>
  /// <remarks>
  /// Four things are gated on being head - deciding a join request, adding a
  /// child, starting a conversion, and seeing the family code - so a household
  /// whose head walked out without succession would be as frozen as one whose
  /// head erased.
  /// </remarks>
  [Fact]
  public void A_head_who_leaves_hands_the_household_to_whoever_is_left()
  {
      var headId = Guid.NewGuid();
      var family = Household(headId);

      var successor = Join(family, family, Start.AddDays(1));

      family.RemoveMember(headId);

      family.SucceedHeadAfterRemoval(headId).Should().Be(successor);
      family.IsHead(successor).Should().BeTrue();
      family.FindMember(headId).Should().BeNull();
  }

  /// <summary>
  /// Active members only, and in joining order. A pending request is not
  /// somebody the household can be handed to, and not somebody whose presence
  /// should stop the last real member leaving.
  /// </summary>
  [Fact]
  public void Active_members_excludes_anybody_who_has_only_asked()
  {
      var headId = Guid.NewGuid();
      var family = Household(headId);

      var joined = Join(family, family, Start.AddDays(1));
      family.RequestJoin(Guid.NewGuid(), Relationship.Sibling, Start.AddDays(2));

      family.ActiveMembers().Select(m => m.MemberProfileId)
          .Should().BeEquivalentTo([headId, joined]);
  }
}
