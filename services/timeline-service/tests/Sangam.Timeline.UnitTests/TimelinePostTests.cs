using FluentAssertions;
using Sangam.Timeline.Domain.Posts;
using Xunit;

namespace Sangam.Timeline.UnitTests;

public sealed class TimelinePostTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid ModeratorId = Guid.NewGuid();

    private static TimelinePost MemberPost() =>
        TimelinePost.Create(
            TenantId, AuthorId, PostType.MemberPost, "Blood donation drive", "Volunteers welcome.", Now);

    private static TimelinePost Announcement() =>
        TimelinePost.Create(
            TenantId, AuthorId, PostType.Announcement, "Paryushan programme", "Schedule attached.", Now);

    private static TimelinePost ApprovedPost()
    {
        var post = MemberPost();
        post.Moderate(ModerationDecision.Approve, ModeratorId, null, Now);
        post.ClearDomainEvents();

        return post;
    }

    // ---- Creation ---------------------------------------------------------

    [Fact]
    public void A_member_post_waits_for_a_moderator()
    {
        // The wireframe's button says "Post for Review", and this is what makes
        // that true rather than a label.
        MemberPost().Status.Should().Be(PostStatus.PendingReview);
    }

    [Fact]
    public void A_member_post_is_not_visible_to_the_Samaaj_until_it_is_approved()
    {
        MemberPost().IsPubliclyVisible.Should().BeFalse();
    }

    [Fact]
    public void An_announcement_is_published_without_review()
    {
        // Only someone who could approve their own post may create one, so a
        // queue step would be a control that is not one.
        var post = Announcement();

        post.Status.Should().Be(PostStatus.Approved);
        post.IsPubliclyVisible.Should().BeTrue();
    }

    [Fact]
    public void Creating_announces_the_post_without_repeating_what_it_says()
    {
        // audit-notification-service records payloads verbatim into an
        // append-only table. Moderation exists because some of what members
        // write should not end up on the timeline; putting the body somewhere
        // deliberately hard to redact would defeat that on the post that most
        // needed it.
        var post = MemberPost();

        var raised = post.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<PostSubmittedDomainEvent>().Subject;

        raised.PostId.Should().Be(post.Id);
        raised.Status.Should().Be(nameof(PostStatus.PendingReview));
    }

    // ---- Moderation -------------------------------------------------------

    [Theory]
    [InlineData(ModerationDecision.Approve, PostStatus.Approved)]
    [InlineData(ModerationDecision.Reject, PostStatus.Rejected)]
    public void A_decision_moves_the_post(ModerationDecision decision, PostStatus expected)
    {
        var post = MemberPost();

        post.Moderate(decision, ModeratorId, "Checked", Now).Should().BeTrue();
        post.Status.Should().Be(expected);
    }

    [Fact]
    public void Deciding_the_same_way_twice_changes_nothing_and_records_nothing()
    {
        // Two moderators reaching the same conclusion is agreement, not a
        // second decision worth a second audit row.
        var post = ApprovedPost();

        post.Moderate(ModerationDecision.Approve, Guid.NewGuid(), null, Now).Should().BeFalse();
        post.ModerationActions.Should().ContainSingle();
    }

    [Fact]
    public void Every_decision_is_kept_with_who_made_it()
    {
        // "Why is this not on the timeline?" needs an answer that does not
        // depend on somebody remembering.
        var post = ApprovedPost();

        post.Moderate(ModerationDecision.Hide, ModeratorId, "Reported as inaccurate", Now);
        post.Moderate(ModerationDecision.Restore, ModeratorId, "Checked with the author", Now);

        post.ModerationActions.Should().HaveCount(3);
        post.ModerationActions.Should().OnlyContain(a => a.ActorUserId == ModeratorId);
    }

    [Fact]
    public void Moderating_announces_the_previous_status_as_well_as_the_new_one()
    {
        var post = MemberPost();
        post.ClearDomainEvents();

        post.Moderate(ModerationDecision.Approve, ModeratorId, null, Now);

        var raised = post.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<PostModeratedDomainEvent>().Subject;

        raised.PreviousStatus.Should().Be(nameof(PostStatus.PendingReview));
        raised.Status.Should().Be(nameof(PostStatus.Approved));
        raised.ActorUserId.Should().Be(ModeratorId);
    }

    [Fact]
    public void Restoring_a_post_clears_the_reports_that_hid_it()
    {
        // Otherwise the next moderator sees a post that looks freshly
        // complained about when the complaints were already answered.
        var post = ApprovedPost();
        post.Report(Guid.NewGuid(), Now);
        post.Moderate(ModerationDecision.Hide, ModeratorId, "Looking into it", Now);

        post.Moderate(ModerationDecision.Restore, ModeratorId, "Nothing wrong with it", Now);

        post.ReportCount.Should().Be(0);
    }

    // ---- Comments ---------------------------------------------------------

    [Fact]
    public void An_approved_post_can_be_commented_on()
    {
        var post = ApprovedPost();

        post.Comment(Guid.NewGuid(), "  Wonderful.  ", Now).Should().NotBeNull();
        post.Comments.Should().ContainSingle().Which.Body.Should().Be("Wonderful.");
    }

    [Theory]
    [InlineData(PostStatus.PendingReview)]
    [InlineData(PostStatus.Rejected)]
    [InlineData(PostStatus.Hidden)]
    public void A_post_nobody_can_see_cannot_be_commented_on(PostStatus status)
    {
        // Not visible means the only way to be commenting on it is to have
        // guessed its id.
        var post = MemberPost();

        if (status != PostStatus.PendingReview)
        {
            post.Moderate(ModerationDecision.Approve, ModeratorId, null, Now);
            post.Moderate(
                status == PostStatus.Rejected ? ModerationDecision.Reject : ModerationDecision.Hide,
                ModeratorId,
                "Because",
                Now);
        }

        post.Comment(Guid.NewGuid(), "Hello", Now).Should().BeNull();
    }

    // ---- Reactions --------------------------------------------------------

    [Fact]
    public void A_member_holds_one_reaction_at_a_time()
    {
        var post = ApprovedPost();
        var member = Guid.NewGuid();

        post.React(member, ReactionType.Appreciate, Now);
        post.React(member, ReactionType.Celebrate, Now);

        post.Reactions.Should().ContainSingle().Which.Type.Should().Be(ReactionType.Celebrate);
    }

    [Fact]
    public void Reacting_the_same_way_again_takes_it_back()
    {
        // What every product with this button does, and what a member will
        // expect without being told.
        var post = ApprovedPost();
        var member = Guid.NewGuid();

        post.React(member, ReactionType.Support, Now);

        post.React(member, ReactionType.Support, Now).Should().BeNull();
        post.Reactions.Should().BeEmpty();
    }

    [Fact]
    public void Two_members_react_independently()
    {
        var post = ApprovedPost();

        post.React(Guid.NewGuid(), ReactionType.Support, Now);
        post.React(Guid.NewGuid(), ReactionType.Support, Now);

        post.Reactions.Should().HaveCount(2);
    }

    // ---- Reporting --------------------------------------------------------

    [Fact]
    public void Reporting_raises_the_count_without_hiding_anything()
    {
        // One member taking a post off a community's timeline would be a
        // heckler's veto. The decision stays with a moderator.
        var post = ApprovedPost();

        post.Report(Guid.NewGuid(), Now).Should().BeTrue();

        post.ReportCount.Should().Be(1);
        post.IsPubliclyVisible.Should().BeTrue();
    }

    [Fact]
    public void Reporting_announces_the_count_and_never_the_reporter()
    {
        // In a community where everyone knows each other, a reporter who could
        // be identified is a reporter who stays quiet.
        var post = ApprovedPost();

        post.Report(Guid.NewGuid(), Now);

        var raised = post.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<PostReportedDomainEvent>().Subject;

        raised.ReportCount.Should().Be(1);
        raised.GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(name => name.Contains("Reporter", StringComparison.Ordinal));
    }

    [Fact]
    public void An_author_cannot_report_their_own_post()
    {
        var post = ApprovedPost();

        post.Report(AuthorId, Now).Should().BeFalse();
        post.ReportCount.Should().Be(0);
    }

    [Fact]
    public void A_post_nobody_can_see_cannot_be_reported()
    {
        MemberPost().Report(Guid.NewGuid(), Now).Should().BeFalse();
    }
}
