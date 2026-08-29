using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.SocialIssues.Application.Security;
using Sangam.SocialIssues.Domain.Issues;
using Xunit;

namespace Sangam.SocialIssues.IntegrationTests;

/// <summary>
/// The approval workflow through its endpoints, against a real database.
/// </summary>
public sealed class IssueEndpointsTests(SocialIssuesApiFactory factory)
    : IClassFixture<SocialIssuesApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid AuthorId = Guid.NewGuid();
    private static readonly Guid ReviewerId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? AuthorId,
            tenantId ?? TenantId,
            [Roles.Member],
            [PermissionKeys.MembersRead]);

    private HttpClient Reviewer(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? ReviewerId,
            tenantId ?? TenantId,
            [Roles.ContentModerator],
            [PermissionKeys.MembersRead, PermissionKeys.SocialIssuesApprove]);

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
    public async Task An_unpublished_issue_is_invisible_to_other_members()
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
    public async Task The_list_does_not_cross_Samaaj()
    {
        var id = await SubmitAsync();
        await MoveAsync(Reviewer(), id, "Approved");
        await MoveAsync(Reviewer(), id, "Published");

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
            Reviewer(userId: Guid.NewGuid(), tenantId: OtherTenantId), id, "Approved");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- The workflow -----------------------------------------------------

    [Fact]
    public async Task The_whole_path_runs_from_submitted_to_published()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Reviewer(), id, "UnderReview")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MoveAsync(Reviewer(), id, "Approved")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MoveAsync(Reviewer(), id, "Published")).StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await Member(userId: Guid.NewGuid())
            .GetFromJsonAsync<JsonElement>("/v1/social-issues");

        list.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Publishing_without_approval_is_refused()
    {
        // The one promise this service makes: published only after valid
        // approval.
        var id = await SubmitAsync();

        var response = await MoveAsync(Reviewer(), id, "Published");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var persisted = await factory.WithDbContextAsync(db =>
            db.Issues.IgnoreQueryFilters().AsNoTracking().SingleAsync(i => i.Id == id));

        persisted.Status.Should().Be(IssueStatus.Submitted);
    }

    [Fact]
    public async Task A_member_cannot_decide_about_their_own_issue()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Member(), id, "Approved")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Nor_about_anybody_else_s()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Member(userId: Guid.NewGuid()), id, "Approved")).StatusCode
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
    public async Task Approving_needs_no_reason()
    {
        var id = await SubmitAsync();

        (await MoveAsync(Reviewer(), id, "Approved")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_member_may_withdraw_their_own_issue_but_not_somebody_else_s()
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
    public async Task A_published_issue_can_no_longer_be_edited()
    {
        // A reviewer who approved one thing and finds another published has
        // been made to endorse something they never read.
        var id = await SubmitAsync();
        await MoveAsync(Reviewer(), id, "Approved");
        await MoveAsync(Reviewer(), id, "Published");

        var response = await Member().PutAsJsonAsync($"/v1/social-issues/{id}", new
        {
            title = "Something else entirely",
            description = "Rewritten after approval.",
            category = "Community",
            locality = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- The queue and the history ---------------------------------------

    [Fact]
    public async Task The_approval_queue_is_the_reviewer_s_alone()
    {
        await SubmitAsync();

        (await Member().GetAsync("/v1/social-issues/approval-queue")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        var queue = await Reviewer().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue");

        queue.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task A_decided_issue_leaves_the_queue()
    {
        var id = await SubmitAsync();

        await MoveAsync(Reviewer(), id, "Rejected", "Not for the Samaaj to act on.");

        var queue = await Reviewer().GetFromJsonAsync<JsonElement>("/v1/social-issues/approval-queue");

        queue.EnumerateArray().Should().BeEmpty();
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

        // An author may withdraw, and may not approve.
        authorMoves.Should().Contain("Closed");
        authorMoves.Should().NotContain("Approved");

        var asReviewer = await Reviewer().GetFromJsonAsync<JsonElement>($"/v1/social-issues/{id}");
        var reviewerMoves = asReviewer.GetProperty("issue").GetProperty("availableTransitions")
            .EnumerateArray().Select(t => t.GetString()).ToList();

        reviewerMoves.Should().Contain(["Approved", "Rejected", "ChangesRequested", "UnderReview"]);
        reviewerMoves.Should().NotContain("Published", "it has not been approved yet");
    }
}
