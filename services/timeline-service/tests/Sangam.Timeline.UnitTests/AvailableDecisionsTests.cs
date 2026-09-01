using FluentAssertions;
using Sangam.Timeline.Domain.Posts;
using Xunit;

namespace Sangam.Timeline.UnitTests;

/// <summary>
/// What a moderation queue offers, which the screen renders and never derives.
/// </summary>
public sealed class AvailableDecisionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Author = Guid.NewGuid();
    private static readonly Guid Moderator = Guid.NewGuid();

    private static TimelinePost Post() =>
        TimelinePost.Create(
            TenantId, Author, PostType.MemberPost, "Community Seva Drive", "Sunday, 8am.", Now);

    [Fact]
    public void A_post_waiting_to_be_seen_can_be_published_or_refused()
    {
        Post().AvailableDecisions.Should().BeEquivalentTo(
            [ModerationDecision.Approve, ModerationDecision.Reject]);
    }

    [Fact]
    public void A_published_post_can_only_be_taken_down()
    {
        // It is in the queue because somebody reported it. Reject is for
        // something that was never published, and the member has already seen
        // this one go up - so Hide is the only honest word for what happens.
        var post = Post();
        post.Moderate(ModerationDecision.Approve, Moderator, null, Now);

        post.AvailableDecisions.Should().BeEquivalentTo([ModerationDecision.Hide]);
    }

    [Fact]
    public void A_refused_post_can_be_reconsidered()
    {
        var post = Post();
        post.Moderate(ModerationDecision.Reject, Moderator, "Off topic.", Now);

        post.AvailableDecisions.Should().BeEquivalentTo([ModerationDecision.Approve]);
    }

    [Fact]
    public void A_hidden_post_is_restored_rather_than_approved_again()
    {
        // Both end at Approved. Restore is the one that makes the moderation
        // history read as what happened: it went up, came down, went back up.
        var post = Post();
        post.Moderate(ModerationDecision.Approve, Moderator, null, Now);
        post.Moderate(ModerationDecision.Hide, Moderator, "Reported by four members.", Now);

        post.AvailableDecisions.Should().BeEquivalentTo([ModerationDecision.Restore]);
    }

    [Fact]
    public void No_decision_ever_offered_would_change_nothing()
    {
        // The point of narrowing the list: a button that leaves the post exactly
        // as it is wastes a moderator's click and adds nothing to the history.
        foreach (var decision in Post().AvailableDecisions)
        {
            var post = Post();

            post.Moderate(decision, Moderator, "Because.", Now)
                .Should().BeTrue("{0} should do something to a pending post", decision);
        }
    }

    [Fact]
    public void The_list_narrows_but_does_not_gate()
    {
        // Moderate stays permissive on purpose: two moderators reaching the same
        // conclusion is agreement, not an error, and the handler reports a
        // no-op as success. The list is about what to put in front of somebody.
        var post = Post();
        post.Moderate(ModerationDecision.Approve, Moderator, null, Now);

        post.AvailableDecisions.Should().NotContain(ModerationDecision.Restore);
        post.Moderate(ModerationDecision.Restore, Moderator, null, Now)
            .Should().BeFalse("it is already Approved, so nothing changes");
    }
}
