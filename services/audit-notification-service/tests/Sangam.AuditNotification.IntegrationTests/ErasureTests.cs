using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.AuditNotification.Infrastructure.Messaging;
using Xunit;

namespace Sangam.AuditNotification.IntegrationTests;

/// <summary>
/// Erasure against a real database.
/// </summary>
/// <remarks>
/// The unit tests substitute the repository, so they prove the handler calls
/// the right things and nothing about whether those things work. Everything
/// interesting here is SQL: ExecuteUpdate has to reach properties with private
/// setters on an aggregate that deliberately exposes no mutating method, and
/// both statements have to see past the tenant query filter, which a consumer
/// can never satisfy because it has no request and so no tenant.
/// </remarks>
public sealed class ErasureTests(AuditNotificationApiFactory factory)
    : IClassFixture<AuditNotificationApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private async Task<Guid> PublishAsync(
        string topic,
        string payload,
        string eventType,
        Guid? messageId = null)
    {
        var id = messageId ?? Guid.NewGuid();

        using var producer = factory.CreateProducer();

        await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = TenantId.ToString(),
            Value = payload,
            Headers =
            [
                new Header(EventHeaders.MessageId, Encoding.UTF8.GetBytes(id.ToString())),
                new Header(EventHeaders.EventType, Encoding.UTF8.GetBytes(eventType)),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(TenantId.ToString())),
                new Header(EventHeaders.OccurredAt, Encoding.UTF8.GetBytes(
                    DateTimeOffset.UtcNow.ToString("O"))),
            ],
        });

        producer.Flush(TimeSpan.FromSeconds(10));

        return id;
    }

    private Task<Guid> PublishRegistrationAsync(Guid userId) =>
        PublishAsync(
            "identity.user.registered.v1",
            $$"""{"userId":"{{userId}}","tenantId":"{{TenantId}}","fullName":"Ravi Shah"}""",
            "Sangam.IdentityTenant.Domain.Users.UserRegisteredDomainEvent");

    private Task<Guid> PublishErasureAsync(Guid userId, Guid? messageId = null) =>
        PublishAsync(
            "identity.user.erased.v1",
            $$"""{"userId":"{{userId}}","tenantId":"{{TenantId}}"}""",
            "Sangam.IdentityTenant.Domain.Users.UserErasedDomainEvent",
            messageId);

    [Fact]
    public async Task An_erasure_event_de_identifies_the_rows_that_member_was_the_actor_on()
    {
        var userId = Guid.NewGuid();
        var registration = await PublishRegistrationAsync(userId);

        await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(a => a.SourceMessageId == registration),
            found => found is not null);

        await PublishErasureAsync(userId);

        var after = await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(a => a.SourceMessageId == registration),
            row => row.ActorUserId is null);

        after.ActorUserId.Should().BeNull();

        // The name was in the registration payload. That is the whole reason
        // the payload cannot simply be kept.
        after.AfterState.Should().NotContain("Ravi Shah");

        // What survives is what makes it an audit row rather than a hole where
        // one used to be.
        after.Action.Should().Be("UserRegistered");
        after.Topic.Should().Be("identity.user.registered.v1");
        after.OccurredAt.Should().NotBe(default);
    }

    [Fact]
    public async Task The_erasure_itself_is_recorded_and_survives_the_de_identifying_pass()
    {
        // A Samaaj has to be able to show it honoured the request. A row
        // written before the update would have been wiped by it.
        var userId = Guid.NewGuid();
        var messageId = await PublishErasureAsync(userId);

        var recorded = await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(a => a.SourceMessageId == messageId),
            found => found is not null);

        recorded.Should().NotBeNull();
        recorded!.Action.Should().Be("Erased");
        recorded.EntityId.Should().Be(userId.ToString());
        recorded.ActorUserId.Should().BeNull();
    }

    [Fact]
    public async Task An_erasure_deletes_that_member_s_notifications()
    {
        var userId = Guid.NewGuid();
        var registration = await PublishRegistrationAsync(userId);

        await factory.EventuallyAsync(
            db => db.Notifications.IgnoreQueryFilters().CountAsync(
                n => n.SourceMessageId == registration),
            count => count == 1);

        await PublishErasureAsync(userId);

        var remaining = await factory.EventuallyAsync(
            db => db.Notifications.IgnoreQueryFilters().CountAsync(
                n => n.RecipientUserId == userId),
            count => count == 0);

        remaining.Should().Be(0);
    }

    [Fact]
    public async Task Another_member_s_rows_are_untouched()
    {
        var erased = Guid.NewGuid();
        var kept = Guid.NewGuid();

        await PublishRegistrationAsync(erased);
        var keptRegistration = await PublishRegistrationAsync(kept);

        await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(a => a.SourceMessageId == keptRegistration),
            found => found is not null);

        await PublishErasureAsync(erased);

        await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters()
                .CountAsync(a => a.ActorUserId == erased),
            count => count == 0);

        var others = await factory.WithDbContextAsync(db =>
            db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.ActorUserId == kept));

        others.Should().Be(1);
    }

    [Fact]
    public async Task The_same_erasure_delivered_twice_records_one_completion_row()
    {
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await PublishErasureAsync(userId, messageId);

        await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.SourceMessageId == messageId),
            count => count == 1);

        await PublishErasureAsync(userId, messageId);

        await Task.Delay(2000);

        var rows = await factory.WithDbContextAsync(db =>
            db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.SourceMessageId == messageId));

        rows.Should().Be(1);
    }
}
