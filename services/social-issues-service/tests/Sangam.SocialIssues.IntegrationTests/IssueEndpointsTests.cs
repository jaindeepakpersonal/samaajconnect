using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.SocialIssues.Application.IntegrationEvents;
using Sangam.SocialIssues.Application.Security;
using Sangam.SocialIssues.Domain.Issues;
using Sangam.SocialIssues.Domain.Panel;
using Xunit;

namespace Sangam.SocialIssues.IntegrationTests;

/// <summary>
/// The review workflow through its endpoints, against a real database - and who
/// is allowed to run it, which is the Samaaj Panch once there is one.
/// </summary>
public sealed class IssueEndpointsTests(SocialIssuesApiFactory factory)
    : IClassFixture<SocialIssuesApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid ReviewerId = Guid.NewGuid();
    private static readonly Guid PanchMemberId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? AuthorId,
            tenantId ?? TenantId,
            [Roles.Member],
            [PermissionKeys.MembersRead]);

    /// <summary>An administrator: holds the reviewer permission, sits on no Panch.</summary>
    private HttpClient Reviewer(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? ReviewerId,
            tenantId ?? TenantId,
            [Roles.ContentModerator],
            [PermissionKeys.MembersRead, PermissionKeys.SocialIssuesApprove]);

    /// <summary>A Panch member: an ordinary member with a seat, and no reviewer permission.</summary>
    private HttpClient Panch(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? PanchMemberId,
            tenantId ?? TenantId,
            [Roles.Member],
            [PermissionKeys.MembersRead]);

    private Task SeatAsync(Guid memberId, Guid? tenantId = null) =>
        factory.WithDbContextAsync(async db =>
        {
            db.PanelMembers.Add(new PanelMember(tenantId ?? TenantId, memberId));
            await db.SaveChangesAsync();

            return 0;
        });

    private static object NewIssue(bool submitNow = true) => new
    {
        title = "Road safety near the community school",
        description = "Cars come through too fast at closing time.",
        category = "Safety",
        locality = "Hiran Magri",
        submitNow,
    };

    private async Task<Guid> SubmitAsync(bool submitNow = true)
    {
        var response = await Member().PostAsJsonAsync("/v1/social-issues", NewIssue(submitNow));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> MoveAsync(
        HttpClient client, Guid id, string status, string? reason = null) =>
        client.PostAsJsonAsync($"/v1/social-issues/{id}/status", new { status, reason });

    private Task<HttpResponseMessage> DecideAsync(
        HttpClient client, Guid id, string? decision = "Repaint the crossing.", string? group = null) =>
        client.PostAsJsonAsync(
            $"/v1/social-issues/{id}/decision", new { decision, monitoringGroupId = group });

    /// <summary>An issue in front of the Panch, with one seated.</summary>
    private async Task<Guid> PendingAsync()
    {
        await SeatAsync(PanchMemberId);

        var id = await SubmitAsync();
        (await MoveAsync(Panch(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.OK);

        return id;
    }

    // ---- Submitting -------------------------------------------------------

    [Fact]
    public async Task Submitting_writes_one_outbox_row_in_the_same_transaction()
    {
        var id = await SubmitAsync();

        var persisted = await factory.WithDbContextAsync(db =>
            db.Issues.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == id));

        persisted.Status.Should().Be(IssueStatus.Submitted);
        persisted.TenantId.Should().Be(TenantId);

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().ToListAsync());

        outbox.Should().ContainSingle();
        outbox[0].Topic.Should().Be("social-issues.issue.submitted.v1");

        // What a member says is wrong in their community is the thing review
        // exists to hold back; it must not reach an append-only log.
        outbox[0].Payload.Should().NotContain("Cars come through");
    }

    [Fact]
    public async Task A_draft_announces_nothing_and_is_the_author_s_alone()
    {
        var id = await SubmitAsync(submitNow: false);

        (await factory.WithDbContextAsync(db => db.OutboxMessages.AsNoTracking().ToListAsync()))
            .Should().BeEmpty();

        (await Member(userId: Guid.NewGuid()).GetAsync($"/v1/social-issues/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Member().GetAsync($"/v1/social-issues/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_category_outside_the_list_is_refused()
    {
        var response = await Member().PostAsJsonAsync("/v1/social-issues", new
        {
            title = "Something",
            description = "Something else.",
            category = "Whatever I like",
            locality = (string?)null,
            submitNow = true,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- Visibility -------------------------------------------------------

    [Fact]
    public async Task A_submitted_issue_is_invisible_to_other_members()
    {
        await SubmitAsync();

        var list = await Member(userId: Guid.NewGuid())
            .GetFromJsonAsync<JsonElement>("/v1/social-issues");

        list.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task But_the_author_sees_their_own_in_the_list()
    {
        // A member who submits something and then cannot see it anywhere
        // concludes it was lost.
        await SubmitAsync();

        var list = await Member().GetFromJsonAsync<JsonElement>("/v1/social-issues");

        var row = list.EnumerateArray().Should().ContainSingle().Subject;

        row.GetProperty("isMine").GetBoolean().Should().BeTrue();
        row.GetProperty("status").GetString().Should().Be("Submitted");
    }

    [Fact]
    public async Task An_issue_in_front_of_the_Panch_is_visible_to_every_member()
    {
        await PendingAsync();

        var list = await Member(userId: Guid.NewGuid())
            .GetFromJsonAsync<JsonElement>("/v1/social-issues");

        list.EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("status").GetString().Should().Be("DecisionPending");
    }

    [Fact]
    public async Task The_list_does_not_cross_Samaaj()
    {
        await PendingAsync();

        var elsewhere = await Member(userId: Guid.NewGuid(), tenantId: OtherTenantId)
            .GetFromJsonAsync<JsonElement>("/v1/social-issues");

        elsewhere.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task An_issue_in_another_Samaaj_cannot_be_moved_even_with_its_id()
    {
        // The IDOR guard. Knowing the id is not access.
        var id = await SubmitAsync();

        var response = await MoveAsync(
            Reviewer(userId: Guid.NewGuid(), tenantId: OtherTenantId), id, "DecisionPending");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_withdrawn_issue_is_not_shown_to_the_Samaaj()
    {
        var id = await SubmitAsync();
        await MoveAsync(Member(), id, "Closed");

        var list = await Member(userId: Guid.NewGuid()).GetFromJsonAsync<JsonElement>("/v1/social-issues");

        list.EnumerateArray().Should().BeEmpty();
    }

    // ---- Who reviews: the Panch, or - until there is one - an administrator ---

    [Fact]
    public async Task With_no_Panch_seated_an_administrator_still_reviews()
    {
        // Otherwise every issue in every Samaaj that has not elected a Panch
        // would stall the day this shipped.
        var id = await SubmitAsync();

        (await MoveAsync(Reviewer(), id, "UnderReview")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MoveAsync(Reviewer(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task With_no_Panch_seated_an_ordinary_member_does_not()
    {
        var id = await SubmitAsync();

        // Not the author, not a reviewer, not on a Panch: an issue that is still
        // private is told to be not found rather than forbidden.
        (await MoveAsync(Panch(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Once_a_Panch_is_seated_an_administrator_can_no_longer_decide_about_issues()
    {
        // "Only the Panch judges" would be a rule with an exception for
        // administrators otherwise, and a rule with an exception does not hold.
        await SeatAsync(PanchMemberId);
        var id = await SubmitAsync();

        (await MoveAsync(Reviewer(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await MoveAsync(Reviewer(), id, "Rejected", "No.")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Panch_member_needs_no_reviewer_permission_to_review()
    {
        await SeatAsync(PanchMemberId);
        var id = await SubmitAsync();

        (await MoveAsync(Panch(), id, "UnderReview")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MoveAsync(Panch(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_Samaaj_s_Panch_does_not_count()
    {
        // Seated in a different Samaaj, this member is nobody's reviewer here.
        await SeatAsync(PanchMemberId, OtherTenantId);
        var id = await SubmitAsync();

        (await MoveAsync(Panch(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And this Samaaj has no Panch, so the administrator still reviews.
        (await MoveAsync(Reviewer(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_review_access_endpoint_says_who_reviews_and_whether_there_is_a_Panch()
    {
        var before = await Panch().GetFromJsonAsync<JsonElement>("/v1/social-issues/review-access");

        before.GetProperty("canReview").GetBoolean().Should().BeFalse();
        before.GetProperty("panelSeated").GetBoolean().Should().BeFalse();

        var admin = await Reviewer().GetFromJsonAsync<JsonElement>("/v1/social-issues/review-access");

        admin.GetProperty("canReview").GetBoolean().Should().BeTrue("no Panch exists to take over");

        await SeatAsync(PanchMemberId);

        var seated = await Panch().GetFromJsonAsync<JsonElement>("/v1/social-issues/review-access");

        seated.GetProperty("canReview").GetBoolean().Should().BeTrue();
        seated.GetProperty("isPanelMember").GetBoolean().Should().BeTrue();
        seated.GetProperty("panelSeated").GetBoolean().Should().BeTrue();

        var adminNow = await Reviewer().GetFromJsonAsync<JsonElement>("/v1/social-issues/review-access");

        adminNow.GetProperty("canReview").GetBoolean().Should().BeFalse("the Panch has taken over");
        adminNow.GetProperty("canSeeQueue").GetBoolean().Should().BeTrue("somebody has to see whether it works");
    }

    // ---- The workflow -----------------------------------------------------

    [Fact]
    public async Task The_whole_path_runs_from_submitted_to_decided()
    {
        await SeatAsync(PanchMemberId);
        var id = await SubmitAsync();

        (await MoveAsync(Panch(), id, "UnderReview")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MoveAsync(Panch(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DecideAsync(Panch(), id, "Repaint the crossing and post a marshal.")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var seenByAnother = await Member(userId: Guid.NewGuid())
            .GetFromJsonAsync<JsonElement>($"/v1/social-issues/{id}");
        var issue = seenByAnother.GetProperty("issue");

        issue.GetProperty("status").GetString().Should().Be("Closed");
        issue.GetProperty("decision").GetString().Should().Be("Repaint the crossing and post a marshal.");
    }

    [Fact]
    public async Task Approving_is_not_a_status_and_publishing_no_longer_exists()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Reviewer(), id, "Approved")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MoveAsync(Reviewer(), id, "Published")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_cannot_decide_about_their_own_issue()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Member(), id, "DecisionPending")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nor_about_anybody_else_s()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Member(userId: Guid.NewGuid()), id, "DecisionPending")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rejecting_without_saying_why_is_refused()
    {
        // Declining somebody's concern about their own community needs an
        // explanation.
        var id = await SubmitAsync();

        (await MoveAsync(Reviewer(), id, "Rejected")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        (await MoveAsync(Reviewer(), id, "Rejected", "Outside the Samaaj's remit."))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Putting_an_issue_in_front_of_the_Panch_needs_no_reason()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Reviewer(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_member_may_withdraw_their_own_issue()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Member(), id, "Closed")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_issue_sent_back_can_be_revised_and_resubmitted()
    {
        var id = await SubmitAsync();

        await MoveAsync(Reviewer(), id, "ChangesRequested", "Please add the road name.");

        var revised = await Member().PutAsJsonAsync($"/v1/social-issues/{id}", new
        {
            title = "Road safety on Hiran Magri main road",
            description = "Cars come through too fast at closing time.",
            category = "Safety",
            locality = "Hiran Magri",
        });

        revised.StatusCode.Should().Be(HttpStatusCode.OK);

        (await MoveAsync(Member(), id, "Submitted")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_issue_in_front_of_the_Panch_can_no_longer_be_edited()
    {
        // A reviewer who approved one thing and finds another being discussed
        // has been made to endorse something they never read.
        var id = await PendingAsync();

        var response = await Member().PutAsJsonAsync($"/v1/social-issues/{id}", new
        {
            title = "Something else entirely",
            description = "Rewritten after approval.",
            category = "Community",
            locality = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- The Panch's decision ---------------------------------------------

    [Fact]
    public async Task A_Panch_member_decides_an_issue_and_hands_it_to_a_group()
    {
        var id = await PendingAsync();
        var group = Guid.NewGuid();

        var response = await DecideAsync(Panch(), id, "Repaint the crossing.", group.ToString());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var issue = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("issue");

        issue.GetProperty("status").GetString().Should().Be("Closed");
        issue.GetProperty("monitoringGroupId").GetGuid().Should().Be(group);
        issue.GetProperty("canDecide").GetBoolean().Should().BeFalse("it is decided now");

        var persisted = await factory.WithDbContextAsync(db =>
            db.Issues.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == id));

        persisted.DecidedBy.Should().Be(PanchMemberId);
        persisted.DecidedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handing_over_the_monitoring_is_optional()
    {
        var id = await PendingAsync();

        (await DecideAsync(Panch(), id)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_a_Panch_member_can_record_a_decision()
    {
        var id = await PendingAsync();

        // An ordinary member: an issue in front of the Panch is visible to them,
        // and they are still not the Panch.
        (await DecideAsync(Member(userId: Guid.NewGuid()), id)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // The author too.
        (await DecideAsync(Member(), id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // And an administrator, now that a Panch exists.
        (await DecideAsync(Reviewer(), id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task With_no_Panch_an_administrator_can_still_decide_what_it_put_forward()
    {
        var id = await SubmitAsync();
        await MoveAsync(Reviewer(), id, "DecisionPending");

        (await DecideAsync(Reviewer(), id)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_decision_without_words_is_refused()
    {
        var id = await PendingAsync();

        (await DecideAsync(Panch(), id, decision: "   ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await DecideAsync(Panch(), id, decision: null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var persisted = await factory.WithDbContextAsync(db =>
            db.Issues.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == id));

        persisted.Status.Should().Be(IssueStatus.DecisionPending);
    }

    [Fact]
    public async Task A_monitoring_group_that_is_not_an_id_is_a_400_not_an_unhandled_one()
    {
        var id = await PendingAsync();

        (await DecideAsync(Panch(), id, group: "not-a-guid")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_issue_that_is_not_pending_cannot_be_decided()
    {
        await SeatAsync(PanchMemberId);
        var id = await SubmitAsync();

        (await DecideAsync(Panch(), id)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_issue_cannot_be_decided_twice()
    {
        var id = await PendingAsync();

        (await DecideAsync(Panch(), id)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DecideAsync(Panch(), id, "A different answer.")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Closing_a_pending_issue_by_status_alone_is_refused()
    {
        // It would close it with no decision.
        var id = await PendingAsync();

        (await MoveAsync(Panch(), id, "Closed")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Another_Samaaj_s_Panch_cannot_decide_this_one()
    {
        var id = await PendingAsync();
        var stranger = Guid.NewGuid();
        await SeatAsync(stranger, OtherTenantId);

        (await DecideAsync(Panch(stranger, OtherTenantId), id)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deciding_announces_it_with_ids_and_never_the_decision_text()
    {
        var id = await PendingAsync();

        await DecideAsync(Panch(), id, "A very private remark about a neighbour.", Guid.NewGuid().ToString());

        var topics = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync());

        var decided = topics.Should().Contain(m => m.Topic == "social-issues.issue.decided.v1").Subject;

        decided.Payload.Should().Contain(PanchMemberId.ToString());
        topics.Should().OnlyContain(m => !m.Payload.Contains("private remark"));
    }

    // ---- The queue and the history ---------------------------------------

    [Fact]
    public async Task The_approval_queue_is_for_reviewers_and_not_for_other_members()
    {
        await SubmitAsync();

        (await Member(userId: Guid.NewGuid()).GetAsync("/v1/social-issues/approval-queue"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var queue = await Reviewer().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue");

        queue.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task A_Panch_member_reads_the_queue_without_the_reviewer_permission()
    {
        await SeatAsync(PanchMemberId);
        await SubmitAsync();

        var queue = await Panch().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue");

        queue.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task An_administrator_still_sees_the_queue_once_a_Panch_exists_but_cannot_act_on_it()
    {
        await SeatAsync(PanchMemberId);
        await SubmitAsync();

        var queue = await Reviewer().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue");

        var row = queue.EnumerateArray().Should().ContainSingle().Subject;

        row.GetProperty("availableTransitions").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task A_rejected_issue_leaves_the_queue()
    {
        var id = await SubmitAsync();

        await MoveAsync(Reviewer(), id, "Rejected", "Not for the Samaaj to act on.");

        var queue = await Reviewer().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue");

        queue.EnumerateArray().Should().BeEmpty();
    }

    /// <summary>
    /// Putting an issue in front of the Panch is not deciding it: it is still
    /// waiting on the Panch, so unlike a rejection it must stay in the queue
    /// rather than fall out of the only screen that can record the decision.
    /// Deciding is what actually clears it.
    /// </summary>
    [Fact]
    public async Task An_issue_in_front_of_the_Panch_stays_in_the_queue_until_it_is_decided()
    {
        var id = await PendingAsync();

        var waiting = await Panch().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue");

        var row = waiting.EnumerateArray().Should().ContainSingle().Which;
        row.GetProperty("status").GetString().Should().Be("DecisionPending");
        row.GetProperty("canDecide").GetBoolean().Should().BeTrue();

        await DecideAsync(Panch(), id);

        var after = await Panch().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue");

        after.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task The_author_can_read_why_theirs_was_rejected()
    {
        // The whole point of keeping the history.
        var id = await SubmitAsync();

        await MoveAsync(Reviewer(), id, "Rejected", "Outside the Samaaj's remit.");

        var detail = await Member().GetFromJsonAsync<JsonElement>($"/v1/social-issues/{id}");
        var history = detail.GetProperty("history").EnumerateArray().ToList();

        history.Should().HaveCount(2);
        history[1].GetProperty("reason").GetString().Should().Be("Outside the Samaaj's remit.");
        history[1].GetProperty("actorUserId").GetGuid().Should().Be(ReviewerId);
    }

    [Fact]
    public async Task The_issue_says_which_moves_this_caller_can_actually_make()
    {
        // So the buttons a screen shows and the moves the server accepts cannot
        // drift apart.
        var id = await SubmitAsync();

        var asAuthor = await Member().GetFromJsonAsync<JsonElement>($"/v1/social-issues/{id}");
        var authorMoves = asAuthor.GetProperty("issue").GetProperty("availableTransitions")
            .EnumerateArray().Select(t => t.GetString()).ToList();

        // An author may withdraw, and may not put it in front of the Panch.
        authorMoves.Should().Contain("Closed");
        authorMoves.Should().NotContain("DecisionPending");

        var asReviewer = await Reviewer().GetFromJsonAsync<JsonElement>($"/v1/social-issues/{id}");
        var reviewerMoves = asReviewer.GetProperty("issue").GetProperty("availableTransitions")
            .EnumerateArray().Select(t => t.GetString()).ToList();

        reviewerMoves.Should().Contain(["DecisionPending", "Rejected", "ChangesRequested", "UnderReview"]);
    }

    // ---- Keeping the roster in step with panch-service --------------------

    private async Task<int> ApplyPanelChangedAsync(Guid tenantId, params Guid[] memberIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var payload = JsonSerializer.Serialize(
            new { tenantId, memberIds, reason = "Elected" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var result = await sender.Send(new ConsumeIntegrationEventCommand(new IntegrationEventEnvelope(
            Guid.NewGuid(), tenantId, "panch.panel.changed.v1", "PanelChangedDomainEvent",
            payload, DateTimeOffset.UtcNow)));

        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }

    private Task<List<Guid>> RosterAsync(Guid tenantId) =>
        factory.WithDbContextAsync(db =>
            db.PanelMembers.IgnoreQueryFilters().AsNoTracking()
                .Where(m => m.TenantId == tenantId).Select(m => m.MemberId).ToListAsync());

    [Fact]
    public async Task A_panel_change_replaces_the_Samaaj_s_roster()
    {
        var first = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var second = new[] { first[2], Guid.NewGuid() };

        await ApplyPanelChangedAsync(TenantId, first);
        (await RosterAsync(TenantId)).Should().BeEquivalentTo(first);

        await ApplyPanelChangedAsync(TenantId, second);
        (await RosterAsync(TenantId)).Should().BeEquivalentTo(second, "the previous Panch is unseated");
    }

    [Fact]
    public async Task Applying_the_same_roster_twice_changes_nothing()
    {
        // Delivery is at-least-once, and a snapshot is what makes that safe.
        var members = new[] { Guid.NewGuid(), Guid.NewGuid() };

        (await ApplyPanelChangedAsync(TenantId, members)).Should().Be(2);
        (await ApplyPanelChangedAsync(TenantId, members)).Should().Be(0, "a redelivery is a no-op");

        (await RosterAsync(TenantId)).Should().BeEquivalentTo(members);
    }

    [Fact]
    public async Task A_panel_change_in_one_Samaaj_leaves_another_alone()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        await ApplyPanelChangedAsync(TenantId, mine);
        await ApplyPanelChangedAsync(OtherTenantId, theirs);
        await ApplyPanelChangedAsync(OtherTenantId);

        (await RosterAsync(TenantId)).Should().BeEquivalentTo([mine]);
        (await RosterAsync(OtherTenantId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Emptying_the_roster_hands_reviewing_back_to_the_administrators()
    {
        await ApplyPanelChangedAsync(TenantId, PanchMemberId);
        var id = await SubmitAsync();

        (await MoveAsync(Reviewer(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await ApplyPanelChangedAsync(TenantId);

        (await MoveAsync(Reviewer(), id, "DecisionPending")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
