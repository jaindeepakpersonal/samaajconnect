using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.AuditNotification.Infrastructure.Messaging;
using Xunit;

namespace Sangam.AuditNotification.IntegrationTests;

/// <summary>
/// The end-to-end claim of this service: an event published by another service
/// lands in the audit log. Real Kafka, real Postgres, real consumer loop.
/// </summary>
public sealed class IntegrationEventConsumerTests(AuditNotificationApiFactory factory)
    : IClassFixture<AuditNotificationApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private async Task<Guid> PublishAsync(
        string topic,
        string payload,
        Guid? messageId = null,
        Guid? tenantId = null,
        string eventType = "Sangam.IdentityTenant.Domain.Users.UserRegisteredDomainEvent",
        bool withHeaders = true)
    {
        var id = messageId ?? Guid.NewGuid();
        var tenant = tenantId ?? TenantId;

        using var producer = factory.CreateProducer();

        var message = new Message<string, string> { Key = tenant.ToString(), Value = payload };

        if (withHeaders)
        {
            message.Headers =
            [
                new Header(EventHeaders.MessageId, Encoding.UTF8.GetBytes(id.ToString())),
                new Header(EventHeaders.EventType, Encoding.UTF8.GetBytes(eventType)),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(tenant.ToString())),
                new Header(EventHeaders.OccurredAt, Encoding.UTF8.GetBytes(
                    DateTimeOffset.UtcNow.ToString("O"))),
            ];
        }

        await producer.ProduceAsync(topic, message);
        producer.Flush(TimeSpan.FromSeconds(10));

        return id;
    }

    [Fact]
    public async Task An_event_published_to_Kafka_is_written_to_the_audit_log()
    {
        var userId = Guid.NewGuid();

        var messageId = await PublishAsync(
            "identity.user.registered.v1",
            $$"""{"userId":"{{userId}}","tenantId":"{{TenantId}}","fullName":"Ravi Shah"}""");

        var recorded = await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(a => a.SourceMessageId == messageId),
            found => found is not null);

        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("UserRegistered");
        recorded.EntityName.Should().Be("User");
        recorded.TenantId.Should().Be(TenantId);
        recorded.ActorUserId.Should().Be(userId);
        recorded.Topic.Should().Be("identity.user.registered.v1");
    }

    [Fact]
    public async Task A_registration_event_also_produces_a_welcome_notification()
    {
        var userId = Guid.NewGuid();

        var messageId = await PublishAsync(
            "identity.user.registered.v1",
            $$"""{"userId":"{{userId}}","tenantId":"{{TenantId}}","fullName":"Meera Shah"}""");

        var notification = await factory.EventuallyAsync(
            db => db.Notifications.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(n => n.SourceMessageId == messageId),
            found => found is not null);

        notification.Should().NotBeNull();
        notification!.RecipientUserId.Should().Be(userId);
        notification.Body.Should().Contain("Meera Shah");
    }

    [Fact]
    public async Task The_same_event_delivered_twice_produces_one_audit_row()
    {
        var messageId = Guid.NewGuid();
        var payload = $$"""{"userId":"{{Guid.NewGuid()}}","tenantId":"{{TenantId}}"}""";

        await PublishAsync("identity.user.registered.v1", payload, messageId);

        await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.SourceMessageId == messageId),
            count => count == 1);

        // Republishing the identical outbox row is exactly what an at-least-once
        // publisher does after a crash between the broker ack and the mark-sent.
        await PublishAsync("identity.user.registered.v1", payload, messageId);

        await Task.Delay(2000);

        var rows = await factory.WithDbContextAsync(db =>
            db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.SourceMessageId == messageId));

        rows.Should().Be(1);
    }

    [Fact]
    public async Task An_event_from_a_service_this_one_has_never_heard_of_is_still_audited()
    {
        var messageId = await PublishAsync(
            "boli.bid.placed.v1",
            $$"""{"bidId":"{{Guid.NewGuid()}}","amount":5100}""",
            eventType: "Sangam.Boli.Domain.Bids.BidPlacedDomainEvent");

        var recorded = await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(a => a.SourceMessageId == messageId),
            found => found is not null);

        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("Placed");
        recorded.EntityName.Should().Be("Bid");
    }

    [Fact]
    public async Task A_message_with_no_headers_is_still_audited_using_its_key_and_coordinates()
    {
        // A publisher that predates the header contract must not create a hole.
        await PublishAsync(
            "identity.user.logged-in.v1",
            $$"""{"userId":"{{Guid.NewGuid()}}","tenantId":"{{TenantId}}"}""",
            withHeaders: false);

        var recorded = await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Topic == "identity.user.logged-in.v1")
                .ToListAsync(),
            found => found.Count > 0);

        recorded.Should().NotBeEmpty();
        recorded[0].TenantId.Should().Be(TenantId);
        recorded[0].Action.Should().Be("UserLoggedIn");
    }
}
