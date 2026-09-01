using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sangam.AuditNotification.Application.Abstractions;
using Sangam.AuditNotification.Application.Notifications.Delivery;
using Sangam.AuditNotification.Domain.Notifications;
using Sangam.AuditNotification.Infrastructure.Messaging;
using Sangam.AuditNotification.Infrastructure.Notifications;
using Sangam.AuditNotification.Infrastructure.Persistence;
using Xunit;

namespace Sangam.AuditNotification.IntegrationTests;

/// <summary>
/// Outbound delivery against a real Postgres.
/// </summary>
/// <remarks>
/// Almost nothing here is provable against a substituted repository. The claim
/// is a single raw UPDATE with FOR UPDATE SKIP LOCKED, chosen precisely because
/// application code cannot do the job; the guarantee it buys - two dispatchers
/// never sending the same message - only exists if that statement behaves the
/// way it is supposed to against the real database. And the attempt counter it
/// increments is what bounds every retry, so a mistake in it is a message sent
/// to a member forever.
/// </remarks>
public sealed class NotificationDeliveryTests(AuditNotificationApiFactory factory)
    : IClassFixture<AuditNotificationApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private NotificationDispatcher Dispatcher =>
        factory.Services.GetRequiredService<NotificationDispatcher>();

    private async Task<Notification> SeedAsync(
        NotificationChannel channel = NotificationChannel.Email,
        string? destination = "ravi@example.com",
        Guid? sourceMessageId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditNotificationDbContext>();

        var notification = Notification.Create(
            TenantId,
            Guid.NewGuid(),
            "Welcome to your Samaaj",
            "Your membership is active.",
            channel,
            sourceMessageId ?? Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            destination);

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        return notification;
    }

    private Task<Notification?> ReloadAsync(Guid id) =>
        factory.WithDbContextAsync(db => db.Notifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == id));

    [Fact]
    public async Task A_pending_notification_is_claimed_delivered_and_marked_sent()
    {
        var seeded = await SeedAsync();

        var handled = await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        handled.Should().BeGreaterThan(0);

        var delivered = await ReloadAsync(seeded.Id);

        delivered!.Status.Should().Be(NotificationStatus.Sent);
        delivered.DeliveredAt.Should().NotBeNull();
        delivered.DeliveryAttempts.Should().Be(1, "the claim counts the attempt, exactly once");
        delivered.DeliveryClaimId.Should().BeNull("a finished row is not held by anyone");
    }

    [Fact]
    public async Task An_in_app_notification_is_never_picked_up_by_the_dispatcher()
    {
        // It was delivered by being written. If the dispatcher could claim it,
        // every in-app message would also be emailed.
        var seeded = await SeedAsync(NotificationChannel.InApp, destination: null);

        await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        var after = await ReloadAsync(seeded.Id);

        after!.Status.Should().Be(NotificationStatus.Sent);
        after.DeliveryAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Two_dispatch_passes_at_once_never_send_the_same_message_twice()
    {
        // The reason the claim is a single conditional UPDATE rather than a read
        // followed by a write. Two passes go for the same Pending rows at the
        // same moment; only one may come away with each.
        //
        // This test was checked by breaking the thing it guards: replacing
        // ClaimPendingAsync with a select-then-update fails it. Removing only
        // FOR UPDATE SKIP LOCKED does not, which is how we know that clause is
        // there for throughput rather than for correctness - the atomicity of
        // the one statement is what stops the double send.
        var seeded = new List<Notification>();

        for (var i = 0; i < 12; i++)
        {
            seeded.Add(await SeedAsync());
        }

        var first = Dispatcher.DispatchBatchAsync(CancellationToken.None);
        var second = Dispatcher.DispatchBatchAsync(CancellationToken.None);

        await Task.WhenAll(first, second);

        foreach (var notification in seeded)
        {
            var after = await ReloadAsync(notification.Id);

            after!.Status.Should().Be(NotificationStatus.Sent);
            after.DeliveryAttempts.Should().Be(
                1, "notification {0} must have been claimed by exactly one pass", notification.Id);
        }
    }

    [Fact]
    public async Task A_notification_abandoned_mid_delivery_goes_back_on_the_queue()
    {
        var seeded = await SeedAsync();

        // What a process killed between claiming and recording leaves behind.
        await ForceStateAsync(
            seeded.Id,
            $"""
             UPDATE notifications
             SET status = 'Sending',
                 delivery_attempts = 1,
                 delivery_claim_id = '{Guid.NewGuid()}',
                 last_attempt_at = now() - interval '30 minutes'
             WHERE id = '{seeded.Id}'
             """);

        var released = await Dispatcher.ReleaseStalledAsync(CancellationToken.None);

        released.Should().BeGreaterThan(0);

        var requeued = await ReloadAsync(seeded.Id);

        requeued!.Status.Should().Be(NotificationStatus.Pending);
        requeued.FailureReason.Should().NotBeNullOrWhiteSpace(
            "an operator needs to see that this one may already have been sent");

        // And it goes out on the next pass, keeping the attempt it already spent.
        await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        var sent = await ReloadAsync(seeded.Id);

        sent!.Status.Should().Be(NotificationStatus.Sent);
        sent.DeliveryAttempts.Should().Be(2);
    }

    [Fact]
    public async Task A_notification_still_in_flight_is_left_alone()
    {
        var seeded = await SeedAsync();

        await ForceStateAsync(
            seeded.Id,
            $"""
             UPDATE notifications
             SET status = 'Sending', delivery_attempts = 1, last_attempt_at = now()
             WHERE id = '{seeded.Id}'
             """);

        await Dispatcher.ReleaseStalledAsync(CancellationToken.None);

        var after = await ReloadAsync(seeded.Id);

        after!.Status.Should().Be(
            NotificationStatus.Sending,
            "reclaiming a message that is merely slow would send it a second time");
    }

    [Fact]
    public async Task A_notification_that_has_used_every_attempt_is_not_claimed_again()
    {
        var seeded = await SeedAsync();

        await ForceStateAsync(
            seeded.Id,
            $"""
             UPDATE notifications
             SET delivery_attempts = {Notification.MaxDeliveryAttempts}
             WHERE id = '{seeded.Id}'
             """);

        await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        var after = await ReloadAsync(seeded.Id);

        after!.Status.Should().Be(NotificationStatus.Pending, "it was never picked up");
        after.DeliveryAttempts.Should().Be(Notification.MaxDeliveryAttempts);
    }

    [Fact]
    public async Task One_event_may_raise_an_in_app_notification_and_an_emailed_copy_of_it()
    {
        var sourceMessageId = Guid.NewGuid();

        await SeedAsync(NotificationChannel.InApp, destination: null, sourceMessageId: sourceMessageId);
        await SeedAsync(NotificationChannel.Email, sourceMessageId: sourceMessageId);

        var rows = await factory.WithDbContextAsync(db => db.Notifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(n => n.SourceMessageId == sourceMessageId)
            .ToListAsync());

        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task The_same_event_cannot_raise_the_same_channel_twice()
    {
        // The dedupe check in the handler is the readable path; this index is
        // what holds when a redelivery arrives while the first copy is still
        // being written.
        var sourceMessageId = Guid.NewGuid();

        await SeedAsync(NotificationChannel.Email, sourceMessageId: sourceMessageId);

        var second = async () => await SeedAsync(
            NotificationChannel.Email, sourceMessageId: sourceMessageId);

        await second.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task A_registration_event_ends_up_as_a_message_addressed_to_the_member()
    {
        // End to end, through the real consumer: an event published to Kafka
        // becomes an in-app notification and an emailed copy, and the emailed
        // one reaches Sent through the real claim.
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        using var producer = factory.CreateProducer();

        await producer.ProduceAsync("identity.user.registered.v1", new Message<string, string>
        {
            Key = TenantId.ToString(),
            Value = $$"""
                      {"userId":"{{userId}}","tenantId":"{{TenantId}}","fullName":"Ravi Shah",
                       "mobileOrEmail":"ravi.shah@example.com"}
                      """,
            Headers =
            [
                new Header(EventHeaders.MessageId, Encoding.UTF8.GetBytes(messageId.ToString())),
                new Header(EventHeaders.EventType, Encoding.UTF8.GetBytes(
                    "Sangam.IdentityTenant.Domain.Users.UserRegisteredDomainEvent")),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(TenantId.ToString())),
                new Header(EventHeaders.OccurredAt, Encoding.UTF8.GetBytes(
                    DateTimeOffset.UtcNow.ToString("O"))),
            ],
        });

        producer.Flush(TimeSpan.FromSeconds(10));

        var raised = await factory.EventuallyAsync(
            db => db.Notifications.IgnoreQueryFilters().AsNoTracking()
                .Where(n => n.SourceMessageId == messageId)
                .ToListAsync(),
            found => found.Count == 2);

        raised.Should().HaveCount(2);

        var email = raised.Single(n => n.Channel == NotificationChannel.Email);

        email.Destination.Should().Be("ravi.shah@example.com");
        email.Status.Should().Be(NotificationStatus.Pending);

        await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        var sent = await ReloadAsync(email.Id);

        sent!.Status.Should().Be(NotificationStatus.Sent);
    }

    [Fact]
    public async Task The_member_sees_one_message_and_the_export_sees_both_copies()
    {
        // The notification list filters to in-app so an emailed copy does not
        // read as a second message. The DPDP s.11 export must not inherit that
        // filter: hiding that a message was also sent to somebody's address, and
        // which address, would make the export less than what is held.
        var userId = Guid.NewGuid();
        var sourceMessageId = Guid.NewGuid();

        await SeedForAsync(userId, NotificationChannel.InApp, null, sourceMessageId);
        await SeedForAsync(userId, NotificationChannel.Email, "ravi@example.com", sourceMessageId);

        var client = factory.CreateClientAs(userId, TenantId, ["Member"], []);

        var list = await client.GetStringAsync("/v1/notifications");
        var export = await client.GetStringAsync("/v1/audit/me/data-export");

        list.Should().NotContain("\"Email\"", "the member is shown the message once");
        list.Should().NotContain("ravi@example.com");

        export.Should().Contain("\"Email\"");
        export.Should().Contain("ravi@example.com", "it is their address, and this service holds it");
    }

    private async Task SeedForAsync(
        Guid recipientUserId, NotificationChannel channel, string? destination, Guid sourceMessageId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditNotificationDbContext>();

        dbContext.Notifications.Add(Notification.Create(
            TenantId, recipientUserId, "Welcome to your Samaaj", "Your membership is active.",
            channel, sourceMessageId, DateTimeOffset.UtcNow, destination));

        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Puts a row into a state no aggregate method will produce - a crashed
    /// claim, an exhausted attempt count - because that is exactly what a crash
    /// or a long-running system leaves behind, and the dispatcher has to cope
    /// with finding it.
    /// </summary>
    private async Task ForceStateAsync(Guid id, string sql)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditNotificationDbContext>();

        var affected = await dbContext.Database.ExecuteSqlRawAsync(sql);

        affected.Should().Be(1, "the test set up nothing if this matched no row");
    }
}

/// <summary>
/// Delivery when the provider refuses, which the logging channel never does.
/// </summary>
/// <remarks>
/// Its own class because it needs a different composition root: the failing
/// channel replaces the registered email one, and a channel registration is not
/// something a test should be able to change under the other tests in the same
/// host.
/// </remarks>
public sealed class NotificationFailureTests(AuditNotificationApiFactory factory)
    : IClassFixture<AuditNotificationApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private WebApplicationFactory<Program> WithChannel(FailingChannel channel) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<INotificationChannel>();
            services.AddSingleton<INotificationChannel>(channel);
        }));

    private static async Task<Guid> SeedAsync(
        IServiceProvider services, int attemptsAlreadyUsed = 0)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditNotificationDbContext>();

        var notification = Notification.Create(
            TenantId, Guid.NewGuid(), "Welcome", "Your membership is active.",
            NotificationChannel.Email, Guid.NewGuid(), DateTimeOffset.UtcNow, "ravi@example.com");

        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync();

        if (attemptsAlreadyUsed > 0)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                $"UPDATE notifications SET delivery_attempts = {attemptsAlreadyUsed} "
                + $"WHERE id = '{notification.Id}'");
        }

        return notification.Id;
    }

    private static async Task<Notification?> ReloadAsync(IServiceProvider services, Guid id)
    {
        // Awaited inside the scope, not returned from it: handing the Task back
        // lets the scope - and the DbContext in it - be disposed before the
        // query has run.
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditNotificationDbContext>();

        return await dbContext.Notifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(n => n.Id == id);
    }

    [Fact]
    public async Task A_transient_failure_leaves_the_message_waiting_for_another_try()
    {
        using var host = WithChannel(new FailingChannel(
            DeliveryResult.Transient("Provider is unreachable.")));

        var id = await SeedAsync(host.Services);

        await host.Services.GetRequiredService<NotificationDispatcher>()
            .DispatchBatchAsync(CancellationToken.None);

        var after = await ReloadAsync(host.Services, id);

        after!.Status.Should().Be(NotificationStatus.Pending);
        after.DeliveryAttempts.Should().Be(1);
        after.FailureReason.Should().Be("Provider is unreachable.");
    }

    [Fact]
    public async Task A_permanent_failure_is_not_tried_again()
    {
        using var host = WithChannel(new FailingChannel(
            DeliveryResult.Permanent("That address does not exist.")));

        var id = await SeedAsync(host.Services);

        await host.Services.GetRequiredService<NotificationDispatcher>()
            .DispatchBatchAsync(CancellationToken.None);

        var after = await ReloadAsync(host.Services, id);

        after!.Status.Should().Be(NotificationStatus.Failed);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_eventually_abandoned()
    {
        // The bound that stops a member being messaged forever. The counter it
        // reads is incremented by the claim statement, so this is the only
        // place the two meet.
        using var host = WithChannel(new FailingChannel(
            DeliveryResult.Transient("Provider is unreachable.")));

        var id = await SeedAsync(host.Services, attemptsAlreadyUsed: Notification.MaxDeliveryAttempts - 1);

        await host.Services.GetRequiredService<NotificationDispatcher>()
            .DispatchBatchAsync(CancellationToken.None);

        var after = await ReloadAsync(host.Services, id);

        after!.DeliveryAttempts.Should().Be(Notification.MaxDeliveryAttempts);
        after.Status.Should().Be(
            NotificationStatus.Failed,
            "a transient failure on the last attempt is still the last attempt");
    }

    [Fact]
    public async Task An_adapter_that_throws_is_treated_as_a_failure_rather_than_stopping_the_batch()
    {
        using var host = WithChannel(FailingChannel.Throwing());

        var id = await SeedAsync(host.Services);

        await host.Services.GetRequiredService<NotificationDispatcher>()
            .DispatchBatchAsync(CancellationToken.None);

        var after = await ReloadAsync(host.Services, id);

        after!.Status.Should().Be(NotificationStatus.Pending, "an exception is treated as transient");
        after.FailureReason.Should().Contain("Exception");
    }
}

internal sealed class FailingChannel(DeliveryResult result, bool throws = false) : INotificationChannel
{
    public static FailingChannel Throwing() =>
        new(DeliveryResult.Delivered(), throws: true);

    public NotificationChannel Channel => NotificationChannel.Email;

    public Task<DeliveryResult> DeliverAsync(
        OutboundMessage message, CancellationToken cancellationToken = default) =>
        throws
            ? throw new InvalidOperationException("The provider client blew up.")
            : Task.FromResult(result);
}
