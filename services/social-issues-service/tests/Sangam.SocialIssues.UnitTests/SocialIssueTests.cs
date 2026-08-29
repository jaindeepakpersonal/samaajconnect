using FluentAssertions;
using Sangam.SocialIssues.Domain.Issues;
using Xunit;

namespace Sangam.SocialIssues.UnitTests;

public sealed class SocialIssueTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid ReviewerId = Guid.NewGuid();

    private static SocialIssue Issue(bool submitNow = true) =>
        SocialIssue.Create(
            TenantId,
            "  Road safety near the community school  ",
            "Cars come through too fast at closing time.",
            "Safety",
            "Hiran Magri",
            AuthorId,
            submitNow,
            Now);

    /// <summary>Walks an issue to a status by making each legal move in turn.</summary>
    private static SocialIssue At(IssueStatus status)
    {
        var issue = Issue();

        if (status == IssueStatus.Submitted)
        {
            return issue;
        }

        if (status is IssueStatus.UnderReview or IssueStatus.Approved
            or IssueStatus.Published or IssueStatus.Closed)
        {
            issue.MoveTo(IssueStatus.UnderReview, ReviewerId, null, Now);
        }

        if (status is IssueStatus.Approved or IssueStatus.Published or IssueStatus.Closed)
        {
            issue.MoveTo(IssueStatus.Approved, ReviewerId, null, Now);
        }

        if (status is IssueStatus.Published or IssueStatus.Closed)
        {
            issue.MoveTo(IssueStatus.Published, ReviewerId, null, Now);
        }

        if (status == IssueStatus.Closed)
        {
            issue.MoveTo(IssueStatus.Closed, ReviewerId, "Dealt with.", Now);
        }

        if (status is IssueStatus.Rejected or IssueStatus.ChangesRequested)
        {
            issue.MoveTo(status, ReviewerId, "Because.", Now);
        }

        issue.Status.Should().Be(status, "the walk should have reached the state under test");
        issue.ClearDomainEvents();

        return issue;
    }

    // ---- Creation ---------------------------------------------------------

    [Fact]
    public void Submitting_starts_the_workflow_and_announces_it()
    {
        var issue = Issue();

        issue.Status.Should().Be(IssueStatus.Submitted);
        issue.Title.Should().Be("Road safety near the community school");

        issue.DomainEvents.Should().ContainSingle()
            .Subject.Should().BeOfType<IssueSubmittedDomainEvent>();
    }

    [Fact]
    public void A_draft_announces_nothing()
    {
        // It is not something the Samaaj or its reviewers have been told about.
        var draft = Issue(submitNow: false);

        draft.Status.Should().Be(IssueStatus.Draft);
        draft.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Creation_is_the_first_row_of_the_history()
    {
        var issue = Issue();

        var first = issue.History.Should().ContainSingle().Subject;

        first.FromStatus.Should().BeNull("nothing preceded creation");
        first.ToStatus.Should().Be(IssueStatus.Submitted);
    }

    [Fact]
    public void The_submitted_event_carries_no_description()
    {
        // What a member says is wrong in their community can name neighbours or
        // be the very thing a reviewer decides not to publish, and
        // audit-notification-service stores payloads verbatim.
        var issue = Issue();

        var raised = issue.DomainEvents.Single();

        raised.GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(["Title", "Description", "Locality"]);
    }

    // ---- The transition table --------------------------------------------

    [Theory]
    [InlineData(IssueStatus.Submitted, IssueStatus.Approved)]
    [InlineData(IssueStatus.Submitted, IssueStatus.Rejected)]
    [InlineData(IssueStatus.Submitted, IssueStatus.UnderReview)]
    [InlineData(IssueStatus.UnderReview, IssueStatus.Approved)]
    [InlineData(IssueStatus.Approved, IssueStatus.Published)]
    [InlineData(IssueStatus.Published, IssueStatus.Closed)]
    public void A_legal_move_is_allowed(IssueStatus from, IssueStatus to)
    {
        At(from).CanMoveTo(to).Should().BeTrue();
    }

    [Theory]
    [InlineData(IssueStatus.Submitted, IssueStatus.Published)]
    [InlineData(IssueStatus.UnderReview, IssueStatus.Published)]
    [InlineData(IssueStatus.Rejected, IssueStatus.Published)]
    [InlineData(IssueStatus.ChangesRequested, IssueStatus.Published)]
    public void Nothing_reaches_Published_except_an_approved_issue(
        IssueStatus from, IssueStatus to)
    {
        // "Member submissions are published only after valid approval" - the
        // subtitle on the member's screen, and the invariant this aggregate
        // exists to hold.
        At(from).CanMoveTo(to).Should().BeFalse();
    }

    [Theory]
    [InlineData(IssueStatus.Published)]
    [InlineData(IssueStatus.Rejected)]
    [InlineData(IssueStatus.Closed)]
    public void A_decided_issue_cannot_go_back_to_the_queue(IssueStatus from)
    {
        var issue = At(from);

        issue.CanMoveTo(IssueStatus.Submitted).Should().BeFalse();
        issue.CanMoveTo(IssueStatus.UnderReview).Should().BeFalse();
    }

    [Fact]
    public void A_move_that_is_not_in_the_table_does_nothing_at_all()
    {
        var issue = At(IssueStatus.Submitted);

        issue.MoveTo(IssueStatus.Published, ReviewerId, null, Now).Should().BeFalse();

        issue.Status.Should().Be(IssueStatus.Submitted);
        issue.History.Should().ContainSingle("a refused move leaves no trace");
    }

    [Fact]
    public void An_issue_sent_back_can_be_resubmitted()
    {
        // Which is the only reason "Request Changes" is worth having.
        var issue = At(IssueStatus.ChangesRequested);

        SocialIssue.IsAuthorMove(IssueStatus.ChangesRequested, IssueStatus.Submitted)
            .Should().BeTrue("it is the author who resubmits, not the reviewer");

        issue.MoveTo(IssueStatus.Submitted, AuthorId, null, Now).Should().BeTrue();
    }

    [Theory]
    [InlineData(IssueStatus.Draft)]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.ChangesRequested)]
    public void A_member_may_withdraw_their_own_issue_before_it_is_published(IssueStatus from)
    {
        SocialIssue.IsAuthorMove(from, IssueStatus.Closed).Should().BeTrue();
    }

    [Fact]
    public void But_closing_a_published_issue_is_the_Samaaj_s_decision()
    {
        // Once the Samaaj has been told, taking it back is not one member's
        // call.
        SocialIssue.IsAuthorMove(IssueStatus.Published, IssueStatus.Closed).Should().BeFalse();
    }

    // ---- History and events ----------------------------------------------

    [Fact]
    public void Every_move_is_recorded_with_who_made_it_and_why()
    {
        // A member whose issue was rejected will ask why, and a Samaaj that
        // cannot answer has failed them twice.
        var issue = At(IssueStatus.UnderReview);

        issue.MoveTo(IssueStatus.Rejected, ReviewerId, "Outside the Samaaj's remit.", Now);

        var last = issue.History.OrderBy(h => h.CreatedAt).Last();

        last.FromStatus.Should().Be(IssueStatus.UnderReview);
        last.ToStatus.Should().Be(IssueStatus.Rejected);
        last.ActorUserId.Should().Be(ReviewerId);
        last.Reason.Should().Be("Outside the Samaaj's remit.");
    }

    [Fact]
    public void Moving_announces_the_previous_status_as_well_as_the_new_one()
    {
        var issue = At(IssueStatus.Submitted);

        issue.MoveTo(IssueStatus.UnderReview, ReviewerId, null, Now);

        var raised = issue.DomainEvents.OfType<IssueStatusChangedDomainEvent>()
            .Should().ContainSingle().Subject;

        raised.PreviousStatus.Should().Be(nameof(IssueStatus.Submitted));
        raised.Status.Should().Be(nameof(IssueStatus.UnderReview));
        raised.SubmittedByMemberId.Should().Be(AuthorId);
        raised.ActorUserId.Should().Be(ReviewerId);
    }

    [Fact]
    public void The_status_change_carries_no_reason_text()
    {
        // A reviewer's note explaining a rejection is written to the member it
        // is about; it belongs in the issue's history, not in a log nobody can
        // redact.
        var issue = At(IssueStatus.UnderReview);

        issue.MoveTo(IssueStatus.Rejected, ReviewerId, "A private remark.", Now);

        issue.DomainEvents.OfType<IssueStatusChangedDomainEvent>().Single()
            .GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain("Reason");
    }

    [Fact]
    public void Publishing_raises_its_own_event_as_well_as_the_status_change()
    {
        // So a consumer that only cares about publication does not have to
        // filter every move in an eight-state workflow.
        var issue = At(IssueStatus.Approved);

        issue.MoveTo(IssueStatus.Published, ReviewerId, null, Now);

        issue.DomainEvents.OfType<IssuePublishedDomainEvent>().Should().ContainSingle();
        issue.DomainEvents.OfType<IssueStatusChangedDomainEvent>().Should().ContainSingle();
        issue.PublishedAt.Should().Be(Now);
    }

    // ---- Visibility and revision -----------------------------------------

    [Theory]
    [InlineData(IssueStatus.Draft, false)]
    [InlineData(IssueStatus.Submitted, false)]
    [InlineData(IssueStatus.UnderReview, false)]
    [InlineData(IssueStatus.Approved, false)]
    [InlineData(IssueStatus.Rejected, false)]
    [InlineData(IssueStatus.Published, true)]
    [InlineData(IssueStatus.Closed, true)]
    public void Only_published_and_closed_issues_are_public(IssueStatus status, bool expected)
    {
        var issue = status == IssueStatus.Draft ? Issue(submitNow: false) : At(status);

        issue.IsPublic.Should().Be(expected);
    }

    [Theory]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview)]
    [InlineData(IssueStatus.ChangesRequested)]
    public void An_undecided_issue_can_still_be_corrected(IssueStatus status)
    {
        At(status).Revise("New title", "New description", "Community", null, Now)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(IssueStatus.Approved)]
    [InlineData(IssueStatus.Published)]
    [InlineData(IssueStatus.Rejected)]
    public void A_decided_issue_cannot_be_edited(IssueStatus status)
    {
        // A reviewer who approved one thing and finds another published has
        // been made to endorse something they never read.
        At(status).Revise("Something else entirely", "Rewritten.", "Community", null, Now)
            .Should().BeFalse();
    }
}
