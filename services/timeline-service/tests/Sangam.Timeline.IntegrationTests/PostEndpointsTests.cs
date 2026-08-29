using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.Timeline.Application.Security;
using Sangam.Timeline.Domain.Posts;
using Xunit;

namespace Sangam.Timeline.IntegrationTests;

/// <summary>
/// The timeline through its endpoints, against a real database.
/// </summary>
/// <remarks>
/// Two things need a real Postgres and cannot be shown against a substitute:
/// the tenant query filter, which is applied by the DbContext rather than by
/// any handler, and the outbox row landing in the same transaction as the post
/// it describes.
/// </remarks>
public sealed class PostEndpointsTests(TimelineApiFactory factory)
    : IClassFixture<TimelineApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid OtherTenantId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Member(Guid? userId = null, Guid? tenantId = null) =>
        factory.CreateClientAs(
            userId ?? MemberId,
            tenantId ?? TenantId,
            [Roles.Member],
            [PermissionKeys.TimelinePost]);

    private HttpClient Moderator(Guid? tenantId = null) =>
        factory.CreateClientAs(
            Guid.NewGuid(),
            tenantId ?? TenantId,
            [Roles.ContentModerator],
            [PermissionKeys.TimelinePost, PermissionKeys.TimelineModerate]);

    private static object NewPost(bool asAnnouncement = false) => new
    {
        title = "Blood donation drive",
        body = "Volunteers are welcome to participate.",
        asAnnouncement,
    };

    private async Task<Guid> PostAsync(HttpClient client, bool asAnnouncement = false)
    {
        var response = await client.PostAsJsonAsync("/v1/timeline/posts", NewPost(asAnnouncement));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    // ---- Posting ----------------------------------------------------------

    [Fact]
    public async Task A_member_post_is_created_pending_and_writes_one_outbox_row_in_the_same_transaction()
    {
        var id = await PostAsync(Member());

        var persisted = await factory.WithDbContextAsync(db =>
            db.Posts.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id));

        persisted.Status.Should().Be(PostStatus.PendingReview);
        persisted.TenantId.Should().Be(TenantId);

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking().ToListAsync());

        outbox.Should().ContainSingle();
        outbox[0].Topic.Should().Be("timeline.post.submitted.v1");

        // The body is the thing moderation exists to hold back. It must not
        // travel to a service that records payloads verbatim in an append-only
        // table.
        outbox[0].Payload.Should().NotContain("Volunteers are welcome");
    }

    [Fact]
    public async Task Posting_without_a_token_is_refused()
    {
        (await factory.CreateClient().PostAsJsonAsync("/v1/timeline/posts", NewPost()))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_cannot_publish_an_announcement()
    {
        // Announcements skip the queue, so asking for one is asking to publish
        // without review.
        var response = await Member().PostAsJsonAsync("/v1/timeline/posts", NewPost(asAnnouncement: true));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_moderator_can_publish_an_announcement_without_review()
    {
        var id = await PostAsync(Moderator(), asAnnouncement: true);

        var persisted = await factory.WithDbContextAsync(db =>
            db.Posts.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id));

        persisted.Status.Should().Be(PostStatus.Approved);
    }

    [Fact]
    public async Task An_empty_post_is_refused()
    {
        var response = await Member().PostAsJsonAsync(
            "/v1/timeline/posts", new { title = "", body = "", asAnnouncement = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---- The feed ---------------------------------------------------------

    [Fact]
    public async Task The_feed_hides_a_post_that_has_not_been_approved()
    {
        await PostAsync(Member());

        var feed = await Member(userId: Guid.NewGuid())
            .GetFromJsonAsync<JsonElement>("/v1/timeline/posts");

        feed.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task But_shows_a_member_their_own_pending_post()
    {
        // The wireframe shows "Your Post • Pending Review" in the same list. A
        // member who posts and then cannot see it anywhere concludes it was
        // lost.
        await PostAsync(Member());

        var feed = await Member().GetFromJsonAsync<JsonElement>("/v1/timeline/posts");

        var post = feed.EnumerateArray().Should().ContainSingle().Subject;

        post.GetProperty("status").GetString().Should().Be(nameof(PostStatus.PendingReview));
    }

    [Fact]
    public async Task An_approved_post_reaches_the_whole_Samaaj()
    {
        var id = await PostAsync(Member());

        await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        var feed = await Member(userId: Guid.NewGuid())
            .GetFromJsonAsync<JsonElement>("/v1/timeline/posts");

        feed.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task The_feed_does_not_cross_Samaaj()
    {
        // The global query filter, which is applied by the DbContext and not by
        // any handler - so only a real database shows it working.
        var id = await PostAsync(Member());

        await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        var elsewhere = await Member(userId: Guid.NewGuid(), tenantId: OtherTenantId)
            .GetFromJsonAsync<JsonElement>("/v1/timeline/posts");

        elsewhere.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task A_post_in_another_Samaaj_cannot_be_moderated_even_with_its_id()
    {
        // The IDOR guard. Knowing the id is not access.
        var id = await PostAsync(Member());

        var response = await Moderator(tenantId: OtherTenantId).PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Moderation -------------------------------------------------------

    [Fact]
    public async Task A_member_cannot_moderate()
    {
        var id = await PostAsync(Member());

        var response = await Member().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rejecting_without_saying_why_is_refused()
    {
        // The member is told this, and "no reason given" is not an answer.
        var id = await PostAsync(Member());

        var response = await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Reject", reason = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approving_without_a_reason_is_fine()
    {
        var id = await PostAsync(Member());

        var response = await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_nonsense_decision_is_refused()
    {
        var id = await PostAsync(Member());

        var response = await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Obliterate", reason = "why not" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Moderating_announces_the_decision()
    {
        var id = await PostAsync(Member());

        await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Reject", reason = "Not for the timeline" });

        var outbox = await factory.WithDbContextAsync(db =>
            db.OutboxMessages.AsNoTracking()
                .Where(m => m.Topic == "timeline.post.moderated.v1")
                .ToListAsync());

        outbox.Should().ContainSingle();

        // A moderator's note about a member is about that member.
        outbox[0].Payload.Should().NotContain("Not for the timeline");
    }

    // ---- The queue --------------------------------------------------------

    [Fact]
    public async Task The_queue_holds_posts_awaiting_review()
    {
        await PostAsync(Member());

        var queue = await Moderator().GetFromJsonAsync<JsonElement>(
            "/v1/timeline/posts/moderation-queue");

        queue.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task The_queue_is_refused_to_a_member()
    {
        (await Member().GetAsync("/v1/timeline/posts/moderation-queue"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_reported_post_comes_back_into_the_queue()
    {
        // A separate "reports" screen is a screen somebody has to remember to
        // open, and the point of a report is that it should not wait for that.
        var id = await PostAsync(Member());

        await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        await Member(userId: Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/report", new { });

        var queue = await Moderator().GetFromJsonAsync<JsonElement>(
            "/v1/timeline/posts/moderation-queue");

        var item = queue.EnumerateArray().Should().ContainSingle().Subject;

        item.GetProperty("post").GetProperty("reportCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_decided_post_leaves_the_queue()
    {
        var id = await PostAsync(Member());

        await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Reject", reason = "Not suitable" });

        var queue = await Moderator().GetFromJsonAsync<JsonElement>(
            "/v1/timeline/posts/moderation-queue");

        queue.EnumerateArray().Should().BeEmpty();
    }

    // ---- Comments, reactions, reports -------------------------------------

    [Fact]
    public async Task An_approved_post_can_be_commented_on()
    {
        var id = await PostAsync(Member());

        await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        var response = await Member(userId: Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/comments", new { body = "Happy to help." });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_pending_post_reports_as_not_found_when_commented_on()
    {
        // Not visible means the only way to be commenting is to have guessed
        // the id. Confirming it exists is the leak.
        var id = await PostAsync(Member());

        var response = await Member(userId: Guid.NewGuid()).PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/comments", new { body = "Hello" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_reaction_can_be_set_and_taken_back()
    {
        var id = await PostAsync(Member());

        await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        var reader = Member(userId: Guid.NewGuid());

        var set = await reader.PutAsJsonAsync(
            $"/v1/timeline/posts/{id}/reaction", new { reaction = "Appreciate" });

        (await set.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("myReaction").GetString().Should().Be("Appreciate");

        var cleared = await reader.PutAsJsonAsync(
            $"/v1/timeline/posts/{id}/reaction", new { reaction = "Appreciate" });

        (await cleared.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("myReaction").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Reporting_answers_the_same_way_whether_or_not_it_counted()
    {
        // A member who learns their own report was ignored has learned how the
        // queue is fed.
        var id = await PostAsync(Member());

        await Moderator().PostAsJsonAsync(
            $"/v1/timeline/posts/{id}/moderate", new { decision = "Approve", reason = (string?)null });

        var byAuthor = await Member().PostAsJsonAsync($"/v1/timeline/posts/{id}/report", new { });
        var bySomeoneElse = await Member(userId: Guid.NewGuid())
            .PostAsJsonAsync($"/v1/timeline/posts/{id}/report", new { });

        byAuthor.StatusCode.Should().Be(HttpStatusCode.OK);
        bySomeoneElse.StatusCode.Should().Be(HttpStatusCode.OK);

        (await byAuthor.Content.ReadAsStringAsync())
            .Should().Be(await bySomeoneElse.Content.ReadAsStringAsync());
    }
}
