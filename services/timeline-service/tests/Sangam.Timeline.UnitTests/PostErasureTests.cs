using FluentAssertions;
using Sangam.Timeline.Domain.Posts;
using Xunit;

namespace Sangam.Timeline.UnitTests;

/// <summary>
/// What erasure does to a post, and what it deliberately leaves alone.
/// </summary>
/// <remarks>
/// The rule is "the words go and the shape stays": a post is a container that
/// other members' comments and reactions hang off, and deleting it would take
/// their records with it.
/// </remarks>
public sealed class PostErasureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid Tenant = Guid.NewGuid();

    private static TimelinePost ApprovedPost(Guid authorId)
    {
        var post = TimelinePost.Create(
            Tenant, authorId, PostType.MemberPost, "Paryushan timings", "Please arrive by six.", Now);

        post.Moderate(ModerationDecision.Approve, Guid.NewGuid(), reason: null, Now);

        return post;
    }

    [Fact]
    public void The_author_words_are_replaced_and_the_post_is_hidden()
    {
        var author = Guid.NewGuid();
        var post = ApprovedPost(author);

        post.ErasePersonalDataOf(author).Should().BeTrue();

        post.Title.Should().Be(TimelinePost.ErasedPlaceholder);
        post.Body.Should().Be(TimelinePost.ErasedPlaceholder);
        post.Status.Should().Be(PostStatus.Hidden);
    }

    [Fact]
    public void Other_members_comments_survive_because_they_are_their_records()
    {
        var author = Guid.NewGuid();
        var somebodyElse = Guid.NewGuid();
        var post = ApprovedPost(author);

        post.Comment(somebodyElse, "Thank you for organising this.", Now);

        post.ErasePersonalDataOf(author);

        post.Comments.Should().ContainSingle()
            .Which.Body.Should().Be("Thank you for organising this.");
    }

    [Fact]
    public void The_erased_member_own_comments_go_wherever_they_left_them()
    {
        // Including on somebody else's post - their words are theirs there too.
        var author = Guid.NewGuid();
        var leaver = Guid.NewGuid();
        var post = ApprovedPost(author);

        post.Comment(leaver, "I will bring the flowers.", Now);

        post.ErasePersonalDataOf(leaver).Should().BeTrue();

        post.Comments.Should().ContainSingle()
            .Which.Body.Should().Be(TimelinePost.ErasedPlaceholder);

        // And the post itself is untouched: its author did not erase.
        post.Title.Should().Be("Paryushan timings");
        post.Status.Should().Be(PostStatus.Approved);
    }

    [Fact]
    public void A_member_who_wrote_nothing_here_changes_nothing()
    {
        var post = ApprovedPost(Guid.NewGuid());

        post.ErasePersonalDataOf(Guid.NewGuid()).Should().BeFalse();

        post.Title.Should().Be("Paryushan timings");
    }

    [Fact]
    public void Erasing_twice_reports_no_change_the_second_time()
    {
        // Delivery is at least once, so the consumer will see this event again.
        var author = Guid.NewGuid();
        var post = ApprovedPost(author);

        post.ErasePersonalDataOf(author).Should().BeTrue();
        post.ErasePersonalDataOf(author).Should().BeFalse();
    }

    [Fact]
    public void Reactions_are_left_alone_because_they_carry_no_words()
    {
        var author = Guid.NewGuid();
        var reactor = Guid.NewGuid();
        var post = ApprovedPost(author);

        post.React(reactor, ReactionType.Appreciate, Now);

        post.ErasePersonalDataOf(author);

        post.Reactions.Should().ContainSingle();
    }
}
