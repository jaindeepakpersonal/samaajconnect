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
    private static readonly Guid PanchMemberId = Guid.NewGuid();

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

    /// <summary>
    /// Walks an issue to a status by making each legal move in turn. <c>Closed</c>
    /// here is the Panch's decision, the one way an issue gets a decision.
    /// </summary>
    private static SocialIssue At(IssueStatus status)
    {
        var issue = Issue();

        if (status == IssueStatus.Submitted)
        {
            return issue;
        }

        if (status is IssueStatus.UnderReview or IssueStatus.DecisionPending or IssueStatus.Closed)
        {
            issue.MoveTo(IssueStatus.UnderReview, ReviewerId, null, Now);
        }

        if (status is IssueStatus.DecisionPending or IssueStatus.Closed)
        {
            issue.MoveTo(IssueStatus.DecisionPending, ReviewerId, null, Now);
        }

        if (status == IssueStatus.Closed)
        {
            issue.RecordDecision("Repaint the crossing.", null, PanchMemberId, Now);
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
        // be the very thing a reviewer decides not to show, and
        // audit-notification-service stores payloads verbatim.
        var issue = Issue();

        var raised = issue.DomainEvents.Single();

        raised.GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(["Title", "Description", "Locality"]);
    }

    // ---- The transition table --------------------------------------------

    [Theory]
    [InlineData(IssueStatus.Submitted, IssueStatus.DecisionPending)]
    [InlineData(IssueStatus.Submitted, IssueStatus.Rejected)]
    [InlineData(IssueStatus.Submitted, IssueStatus.UnderReview)]
    [InlineData(IssueStatus.UnderReview, IssueStatus.DecisionPending)]
    [InlineData(IssueStatus.Rejected, IssueStatus.Closed)]
    public void A_legal_move_is_allowed(IssueStatus from, IssueStatus to)
    {
        At(from).CanMoveTo(to).Should().BeTrue();
    }

    [Theory]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview)]
    [InlineData(IssueStatus.Rejected)]
    [InlineData(IssueStatus.ChangesRequested)]
    public void Nothing_reaches_DecisionPending_except_a_reviewed_issue(IssueStatus from)
    {
        // Only an issue a reviewer has looked at goes in front of the Panch.
        // Rejected and sent-back ones never do.
        var reachable = At(from).CanMoveTo(IssueStatus.DecisionPending);

        reachable.Should().Be(from is IssueStatus.Submitted or IssueStatus.UnderReview);
    }

    [Fact]
    public void Closing_a_pending_issue_is_not_a_plain_move()
    {
        // DecisionPending -> Closed needs the Panch's decision text, so it is
        // not a row in the table: a plain status move that closed it would
        // leave a decided issue with no decision.
        var issue = At(IssueStatus.DecisionPending);

        issue.CanMoveTo(IssueStatus.Closed).Should().BeFalse();
        issue.MoveTo(IssueStatus.Draft, ReviewerId, null, Now).Should().BeFalse();
        issue.Status.Should().Be(IssueStatus.DecisionPending);
    }

    [Theory]
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

        issue.MoveTo(IssueStatus.Draft, ReviewerId, null, Now).Should().BeFalse();

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
    public void A_member_may_withdraw_their_own_issue_before_the_Panch_has_it(IssueStatus from)
    {
        SocialIssue.IsAuthorMove(from, IssueStatus.Closed).Should().BeTrue();
    }

    [Fact]
    public void Once_the_Panch_has_it_the_author_cannot_take_it_back()
    {
        // The Samaaj has been told and the Panch is discussing it; withdrawing
        // is not one member's call any more.
        At(IssueStatus.DecisionPending).CanMoveTo(IssueStatus.Closed).Should().BeFalse();
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

    // ---- The Panch's decision --------------------------------------------

    [Fact]
    public void The_Panch_decides_a_pending_issue_and_closes_it()
    {
        var issue = At(IssueStatus.DecisionPending);
        var group = Guid.NewGuid();

        issue.RecordDecision("  Repaint the crossing and post a marshal.  ", group, PanchMemberId, Now)
            .Should().BeTrue();

        issue.Status.Should().Be(IssueStatus.Closed);
        issue.Decision.Should().Be("Repaint the crossing and post a marshal.");
        issue.MonitoringGroupId.Should().Be(group);
        issue.DecidedAt.Should().Be(Now);
        issue.DecidedBy.Should().Be(PanchMemberId);
    }

    [Fact]
    public void Handing_over_the_monitoring_is_optional()
    {
        var issue = At(IssueStatus.DecisionPending);

        issue.RecordDecision("Nothing further is needed.", null, PanchMemberId, Now).Should().BeTrue();

        issue.MonitoringGroupId.Should().BeNull();
    }

    [Theory]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview)]
    [InlineData(IssueStatus.Rejected)]
    [InlineData(IssueStatus.Closed)]
    public void Only_a_pending_issue_can_be_decided(IssueStatus status)
    {
        var issue = At(status);
        var before = issue.History.Count;

        issue.RecordDecision("Anything.", null, PanchMemberId, Now).Should().BeFalse();

        issue.Status.Should().Be(status);
        issue.History.Should().HaveCount(before, "a refused decision leaves no trace");
    }

    [Fact]
    public void A_decision_needs_words()
    {
        var issue = At(IssueStatus.DecisionPending);

        var attempt = () => issue.RecordDecision("   ", null, PanchMemberId, Now);

        attempt.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deciding_is_recorded_in_the_history_against_the_Panch_member()
    {
        var issue = At(IssueStatus.DecisionPending);

        issue.RecordDecision("Done.", null, PanchMemberId, Now);

        var last = issue.History.OrderBy(h => h.CreatedAt).Last();

        last.FromStatus.Should().Be(IssueStatus.DecisionPending);
        last.ToStatus.Should().Be(IssueStatus.Closed);
        last.ActorUserId.Should().Be(PanchMemberId);
    }

    [Fact]
    public void Deciding_raises_its_own_event_as_well_as_the_status_change()
    {
        var issue = At(IssueStatus.DecisionPending);
        var group = Guid.NewGuid();

        issue.RecordDecision("Done.", group, PanchMemberId, Now);

        issue.DomainEvents.OfType<IssueStatusChangedDomainEvent>().Should().ContainSingle();

        var decided = issue.DomainEvents.OfType<IssueDecidedDomainEvent>().Should().ContainSingle().Subject;

        decided.Topic.Should().Be("social-issues.issue.decided.v1");
        decided.DecidedByMemberId.Should().Be(PanchMemberId);
        decided.MonitoringGroupId.Should().Be(group);
        decided.SubmittedByMemberId.Should().Be(AuthorId);
    }

    [Fact]
    public void The_decided_event_carries_ids_and_never_the_decision_text()
    {
        // The decision is a paragraph about a member's concern, and
        // audit-notification-service records payloads verbatim.
        var issue = At(IssueStatus.DecisionPending);

        issue.RecordDecision("A private remark.", null, PanchMemberId, Now);

        issue.DomainEvents.OfType<IssueDecidedDomainEvent>().Single()
            .GetType().GetProperties().Select(p => p.Name)
            .Should().NotContain(["Decision", "Title", "Description", "Locality"]);
    }

    // ---- Visibility and revision -----------------------------------------

    [Fact]
    public void An_issue_in_front_of_the_Panch_is_public()
    {
        At(IssueStatus.DecisionPending).IsPublic.Should().BeTrue();
    }

    [Fact]
    public void A_decided_issue_stays_public()
    {
        At(IssueStatus.Closed).IsPublic.Should().BeTrue();
    }

    [Theory]
    [InlineData(IssueStatus.Draft)]
    [InlineData(IssueStatus.Submitted)]
    [InlineData(IssueStatus.UnderReview)]
    [InlineData(IssueStatus.Rejected)]
    [InlineData(IssueStatus.ChangesRequested)]
    public void Before_the_Panch_has_it_an_issue_is_private(IssueStatus status)
    {
        var issue = status == IssueStatus.Draft ? Issue(submitNow: false) : At(status);

        issue.IsPublic.Should().BeFalse();
    }

    [Fact]
    public void A_withdrawn_issue_has_no_decision_and_stays_private()
    {
        // It used to be public simply for being Closed. Withdrawing something
        // does not make it the Samaaj's business.
        var issue = Issue();

        issue.MoveTo(IssueStatus.Closed, AuthorId, null, Now);

        issue.Status.Should().Be(IssueStatus.Closed);
        issue.IsPublic.Should().BeFalse();
    }

    [Fact]
    public void A_rejected_issue_that_is_then_closed_stays_private()
    {
        var issue = At(IssueStatus.Rejected);

        issue.MoveTo(IssueStatus.Closed, ReviewerId, null, Now);

        issue.IsPublic.Should().BeFalse();
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
    [InlineData(IssueStatus.DecisionPending)]
    [InlineData(IssueStatus.Closed)]
    [InlineData(IssueStatus.Rejected)]
    public void An_issue_the_Panch_has_or_has_decided_cannot_be_edited(IssueStatus status)
    {
        // A reviewer who approved one thing and finds another being discussed
        // has been made to endorse something they never read.
        At(status).Revise("Something else entirely", "Rewritten.", "Community", null, Now)
            .Should().BeFalse();
    }
}
