using FluentAssertions;
using Sangam.SocialIssues.Domain.Issues;
using Xunit;

namespace Sangam.SocialIssues.UnitTests;

/// <summary>
/// What erasure does to an issue, and what it deliberately leaves alone.
/// </summary>
/// <remarks>
/// The rule is "the words go and the shape stays": an issue is a container that
/// a reviewer's decisions hang off, and those are the reviewer's records rather
/// than the submitter's.
/// </remarks>
public sealed class IssueErasureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid Tenant = Guid.NewGuid();

    private static SocialIssue Submitted(Guid submitterId)
    {
        var issue = SocialIssue.Create(
            Tenant,
            "Road safety near the community school",
            "Cars come through too fast at closing time.",
            "Safety",
            "Hiran Magri",
            submitterId,
            submitNow: true,
            Now);

        return issue;
    }

    [Fact]
    public void The_submitter_words_are_replaced()
    {
        var submitter = Guid.NewGuid();
        var issue = Submitted(submitter);

        issue.ErasePersonalDataOf(submitter).Should().BeTrue();

        issue.Title.Should().Be(SocialIssue.ErasedPlaceholder);
        issue.Description.Should().Be(SocialIssue.ErasedPlaceholder);

        // A locality is a place, but a small enough one to point at a household.
        issue.Locality.Should().BeNull();
    }

    [Fact]
    public void The_status_is_not_moved()
    {
        // A published issue that vanished would leave a Samaaj wondering what
        // happened to something it was told about. What it said is gone; that it
        // existed is not the submitter's alone to erase.
        var submitter = Guid.NewGuid();
        var issue = Submitted(submitter);
        var reviewer = Guid.NewGuid();

        issue.MoveTo(IssueStatus.UnderReview, reviewer, null, Now);
        issue.MoveTo(IssueStatus.Approved, reviewer, null, Now);

        issue.ErasePersonalDataOf(submitter);

        issue.Status.Should().Be(IssueStatus.Approved);
    }

    [Fact]
    public void A_reviewer_reasons_survive_because_they_are_the_reviewer_record()
    {
        var submitter = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var issue = Submitted(submitter);

        issue.MoveTo(IssueStatus.UnderReview, reviewer, null, Now);
        issue.MoveTo(IssueStatus.Rejected, reviewer, "Already raised with the municipality.", Now);

        issue.ErasePersonalDataOf(submitter);

        issue.History.Should().Contain(h =>
            h.ActorUserId == reviewer && h.Reason == "Already raised with the municipality.");
    }

    [Fact]
    public void The_submitter_own_reasons_go()
    {
        // A submitter is an actor in their own workflow - resubmitting after
        // changes were asked for - and those words are theirs like any other.
        var submitter = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        var issue = Submitted(submitter);

        issue.MoveTo(IssueStatus.UnderReview, reviewer, null, Now);
        issue.MoveTo(IssueStatus.ChangesRequested, reviewer, "Please add the street name.", Now);
        issue.MoveTo(IssueStatus.Submitted, submitter, "Added it - it is the lane behind the temple.", Now);

        issue.ErasePersonalDataOf(submitter);

        issue.History.Should().NotContain(h => h.Reason != null && h.Reason.Contains("temple"));
        issue.History.Should().Contain(h => h.Reason == "Please add the street name.");
    }

    [Fact]
    public void A_member_who_wrote_nothing_here_changes_nothing()
    {
        var issue = Submitted(Guid.NewGuid());

        issue.ErasePersonalDataOf(Guid.NewGuid()).Should().BeFalse();

        issue.Title.Should().Be("Road safety near the community school");
    }

    [Fact]
    public void Erasing_twice_reports_no_change_the_second_time()
    {
        // Delivery is at least once, so the consumer will see this event again.
        var submitter = Guid.NewGuid();
        var issue = Submitted(submitter);

        issue.ErasePersonalDataOf(submitter).Should().BeTrue();
        issue.ErasePersonalDataOf(submitter).Should().BeFalse();
    }
}
