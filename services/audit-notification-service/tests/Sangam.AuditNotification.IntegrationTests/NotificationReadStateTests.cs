using System.Net;
using System.Net.Http.Json;
using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.AuditNotification.Application.Security;
using Sangam.AuditNotification.Domain.Notifications;
using Sangam.AuditNotification.Infrastructure.Messaging;
using Sangam.AuditNotification.Infrastructure.Persistence;
using Xunit;

namespace Sangam.AuditNotification.IntegrationTests;

/// <summary>
/// Read state, broadcasts, and the guarantees that only exist in the database.
/// </summary>
/// <remarks>
/// The unique index on (notification_id, user_id) is what makes reading the same
/// notification twice at once a no-op rather than a 500, and the cascade is what
/// stops an erased member leaving read rows pointing at nothing. Neither is
/// provable against a substituted repository.
/// </remarks>
public sealed class NotificationReadStateTests(AuditNotificationApiFactory factory)
    : IClassFixture<AuditNotificationApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static readonly string[] MemberRoles = ["Member"];
    private static readonly string[] AdminRoles = ["SamaajAdmin"];
    private static readonly string[] BroadcastPermission = [PermissionKeys.NotificationsBroadcast];

    private async Task<Notification> SeedAsync(Guid? recipient, Guid? tenantId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditNotificationDbContext>();

        var notification = Notification.Create(
            tenantId ?? TenantId,
            recipient,
            "Paryushan schedule",
            "Timings for the week.",
            NotificationChannel.InApp,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        return notification;
    }

    private HttpClient AsMember(Guid userId) =>
        factory.CreateClientAs(userId, TenantId, MemberRoles, []);

    private HttpClient AsAdmin(Guid userId) =>
        factory.CreateClientAs(userId, TenantId, AdminRoles, BroadcastPermission);

    private static Task<HttpResponseMessage> MarkReadAsync(HttpClient client, Guid id) =>
        client.PostAsync($"/v1/notifications/{id}/read", null);

    [Fact]
    public async Task One_member_reading_a_broadcast_leaves_it_unread_for_everybody_else()
    {
        // The whole reason read state is a row of its own. A flag on the
        // notification would have been set by the first person to open it.
        var broadcast = await SeedAsync(recipient: null);

        var reader = Guid.NewGuid();
        var other = Guid.NewGuid();

        (await MarkReadAsync(AsMember(reader), broadcast.Id)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var readerSees = await AsMember(reader).GetFromJsonAsync<List<NotificationRow>>("/v1/notifications");
        var otherSees = await AsMember(other).GetFromJsonAsync<List<NotificationRow>>("/v1/notifications");

        readerSees!.Single(n => n.Id == broadcast.Id).ReadAt.Should().NotBeNull();
        otherSees!.Single(n => n.Id == broadcast.Id).ReadAt.Should().BeNull();
    }

    [Fact]
    public async Task Reading_the_same_notification_twice_at_once_is_not_an_error()
    {
        // Two tabs, a double tap, a client retrying. A check followed by an
        // insert would let both past the check and leave the unique index to
        // turn the loser into a 500.
        var userId = Guid.NewGuid();
        var notification = await SeedAsync(recipient: userId);
        var client = AsMember(userId);

        var responses = await Task.WhenAll(
            MarkReadAsync(client, notification.Id),
            MarkReadAsync(client, notification.Id),
            MarkReadAsync(client, notification.Id));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

        var rows = await factory.WithDbContextAsync(db => db.NotificationReads
            .IgnoreQueryFilters()
            .CountAsync(r => r.NotificationId == notification.Id));

        rows.Should().Be(1);
    }

    [Fact]
    public async Task Mark_all_as_read_clears_the_list_and_nobody_elses()
    {
        var userId = Guid.NewGuid();
        var bystander = Guid.NewGuid();

        await SeedAsync(recipient: userId);
        await SeedAsync(recipient: userId);
        var broadcast = await SeedAsync(recipient: null);
        var somebodyElses = await SeedAsync(recipient: bystander);

        var response = await AsMember(userId).PostAsync("/v1/notifications/read-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await AsMember(userId).GetFromJsonAsync<List<NotificationRow>>("/v1/notifications");

        mine!.Should().OnlyContain(n => n.ReadAt != null);
        mine.Should().Contain(n => n.Id == broadcast.Id);

        // The button clears the caller's list. It must not touch a message it
        // was never showing them.
        var theirs = await factory.WithDbContextAsync(db => db.NotificationReads
            .IgnoreQueryFilters()
            .AnyAsync(r => r.NotificationId == somebodyElses.Id));

        theirs.Should().BeFalse();
    }

    [Fact]
    public async Task Mark_all_as_read_leaves_the_time_an_earlier_read_actually_happened()
    {
        // Otherwise pressing it would rewrite the history of when things were
        // opened, which is the only thing those timestamps are for.
        var userId = Guid.NewGuid();
        var notification = await SeedAsync(recipient: userId);

        await MarkReadAsync(AsMember(userId), notification.Id);

        var firstRead = await factory.WithDbContextAsync(db => db.NotificationReads
            .IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.NotificationId == notification.Id).Select(r => r.ReadAt).SingleAsync());

        await AsMember(userId).PostAsync("/v1/notifications/read-all", null);

        var afterwards = await factory.WithDbContextAsync(db => db.NotificationReads
            .IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.NotificationId == notification.Id).Select(r => r.ReadAt).SingleAsync());

        afterwards.Should().Be(firstRead);
    }

    [Fact]
    public async Task A_member_cannot_mark_another_members_notification_read()
    {
        var notification = await SeedAsync(recipient: Guid.NewGuid());

        var response = await MarkReadAsync(AsMember(Guid.NewGuid()), notification.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_member_cannot_mark_another_Samaajs_broadcast_read()
    {
        // A broadcast has no recipient, so only the tenant check refuses this.
        var broadcast = await SeedAsync(recipient: null, tenantId: Guid.NewGuid());

        var response = await MarkReadAsync(AsMember(Guid.NewGuid()), broadcast.Id);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_administrator_broadcasts_and_every_member_sees_it_once()
    {
        var admin = Guid.NewGuid();

        var response = await AsAdmin(admin).PostAsJsonAsync(
            "/v1/notifications/broadcast",
            new { title = "Paryushan schedule", body = "Timings for the week." });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var sent = await response.Content.ReadFromJsonAsync<BroadcastRow>();

        var seen = await AsMember(Guid.NewGuid())
            .GetFromJsonAsync<List<NotificationRow>>("/v1/notifications");

        seen!.Where(n => n.Id == sent!.Id).Should().ContainSingle()
            .Which.IsBroadcast.Should().BeTrue();
    }

    [Fact]
    public async Task A_member_cannot_broadcast()
    {
        var response = await AsMember(Guid.NewGuid()).PostAsJsonAsync(
            "/v1/notifications/broadcast",
            new { title = "Free money", body = "Send me your bank details." });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_announcement_with_no_message_is_refused_before_anyone_sees_it()
    {
        var response = await AsAdmin(Guid.NewGuid()).PostAsJsonAsync(
            "/v1/notifications/broadcast", new { title = "Paryushan schedule", body = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_broadcast_list_counts_how_many_members_have_opened_each()
    {
        // The wireframe's Status column says "Delivered", which for an in-app
        // announcement is true the moment the row exists and so says nothing.
        var admin = Guid.NewGuid();

        var sent = await (await AsAdmin(admin).PostAsJsonAsync(
                "/v1/notifications/broadcast",
                new { title = "Counted announcement", body = "Two people will read this." }))
            .Content.ReadFromJsonAsync<BroadcastRow>();

        await MarkReadAsync(AsMember(Guid.NewGuid()), sent!.Id);
        await MarkReadAsync(AsMember(Guid.NewGuid()), sent.Id);

        var listed = await AsAdmin(admin)
            .GetFromJsonAsync<List<BroadcastListRow>>("/v1/notifications/broadcasts");

        listed!.Single(b => b.Id == sent.Id).ReadCount.Should().Be(2);
    }

    [Fact]
    public async Task Erasing_a_member_removes_what_they_had_read_of_the_Samaajs_announcements()
    {
        // The broadcast itself belongs to the Samaaj and stays. The row saying
        // this person opened it on Tuesday is a record of their behaviour, and
        // deleting their own notifications does not reach it.
        var userId = Guid.NewGuid();
        var broadcast = await SeedAsync(recipient: null);

        (await MarkReadAsync(AsMember(userId), broadcast.Id)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        await PublishErasureAsync(userId);

        var remaining = await factory.EventuallyAsync(
            db => db.NotificationReads.IgnoreQueryFilters().CountAsync(r => r.UserId == userId),
            count => count == 0);

        remaining.Should().Be(0);

        var survived = await factory.WithDbContextAsync(db => db.Notifications
            .IgnoreQueryFilters().AnyAsync(n => n.Id == broadcast.Id));

        survived.Should().BeTrue("the announcement was to the whole Samaaj, not to them");
    }

    [Fact]
    public async Task A_broadcast_becomes_an_audit_row_naming_who_sent_it()
    {
        // It goes out through this service's own outbox and comes back through
        // its own consumer. Without it, one person messaging an entire Samaaj
        // would leave no trace with an actor on it.
        var admin = Guid.NewGuid();

        var sent = await (await AsAdmin(admin).PostAsJsonAsync(
                "/v1/notifications/broadcast",
                new { title = "Audited announcement", body = "This should be traceable." }))
            .Content.ReadFromJsonAsync<BroadcastRow>();

        var audited = await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Action == "BroadcastSent" && a.EntityId == sent!.Id.ToString())
                .ToListAsync(),
            found => found.Count > 0,
            TimeSpan.FromSeconds(45));

        audited.Should().ContainSingle()
            .Which.ActorUserId.Should().Be(admin);
    }

    private async Task PublishErasureAsync(Guid userId)
    {
        using var producer = factory.CreateProducer();

        await producer.ProduceAsync("identity.user.erased.v1", new Message<string, string>
        {
            Key = TenantId.ToString(),
            Value = $$"""{"userId":"{{userId}}","tenantId":"{{TenantId}}"}""",
            Headers =
            [
                new Header(EventHeaders.MessageId, Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())),
                new Header(EventHeaders.EventType, Encoding.UTF8.GetBytes(
                    "Sangam.IdentityTenant.Domain.Users.UserErasedDomainEvent")),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(TenantId.ToString())),
                new Header(EventHeaders.OccurredAt, Encoding.UTF8.GetBytes(
                    DateTimeOffset.UtcNow.ToString("O"))),
            ],
        });

        producer.Flush(TimeSpan.FromSeconds(10));
    }

    private sealed record NotificationRow(
        Guid Id, string Title, string Channel, bool IsBroadcast, DateTimeOffset? ReadAt);

    private sealed record BroadcastRow(Guid Id, DateTimeOffset SentAt);

    private sealed record BroadcastListRow(Guid Id, string Title, int ReadCount);
}
