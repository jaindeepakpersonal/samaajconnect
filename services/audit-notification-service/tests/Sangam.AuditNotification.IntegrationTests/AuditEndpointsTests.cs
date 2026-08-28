using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.AuditNotification.Application.Security;
using Sangam.AuditNotification.Infrastructure.Messaging;
using Xunit;

namespace Sangam.AuditNotification.IntegrationTests;

public sealed class AuditEndpointsTests(AuditNotificationApiFactory factory)
    : IClassFixture<AuditNotificationApiFactory>, IAsyncLifetime
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();
    private static readonly Guid MemberA = Guid.NewGuid();

    private Guid _messageA;
    private Guid _messageB;

    public async Task InitializeAsync()
    {
        _messageA = await PublishRegistrationAsync(TenantA, MemberA, "Ravi Shah");
        _messageB = await PublishRegistrationAsync(TenantB, Guid.NewGuid(), "Someone Else");

        await factory.EventuallyAsync(
            db => db.AuditLogs.IgnoreQueryFilters()
                .CountAsync(a => a.SourceMessageId == _messageA || a.SourceMessageId == _messageB),
            count => count == 2);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> PublishRegistrationAsync(Guid tenantId, Guid userId, string name)
    {
        var messageId = Guid.NewGuid();

        using var producer = factory.CreateProducer();

        await producer.ProduceAsync("identity.user.registered.v1", new Message<string, string>
        {
            Key = tenantId.ToString(),
            Value = $$"""{"userId":"{{userId}}","tenantId":"{{tenantId}}","fullName":"{{name}}"}""",
            Headers =
            [
                new Header(EventHeaders.MessageId, Encoding.UTF8.GetBytes(messageId.ToString())),
                new Header(EventHeaders.EventType, Encoding.UTF8.GetBytes("UserRegisteredDomainEvent")),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(tenantId.ToString())),
                new Header(EventHeaders.OccurredAt, Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"))),
            ],
        });

        producer.Flush(TimeSpan.FromSeconds(10));

        return messageId;
    }

    private HttpClient AdminOf(Guid tenantId) =>
        factory.CreateClientAs(Guid.NewGuid(), tenantId, ["SamaajAdmin"], [PermissionKeys.AuditRead]);

    [Fact]
    public async Task Reading_the_audit_log_needs_a_token()
    {
        (await factory.CreateClient().GetAsync("/v1/audit/logs"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_cannot_read_the_Samaaj_audit_log()
    {
        var member = factory.CreateClientAs(MemberA, TenantA, ["Member"], []);

        (await member.GetAsync("/v1/audit/logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_without_the_Audit_Read_permission_is_refused()
    {
        var admin = factory.CreateClientAs(Guid.NewGuid(), TenantA, ["SamaajAdmin"], []);

        (await admin.GetAsync("/v1/audit/logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_Samaaj_admin_sees_only_their_own_Samaaj_rows()
    {
        var logs = await AdminOf(TenantA).GetFromJsonAsync<JsonElement>("/v1/audit/logs");

        var tenants = logs.EnumerateArray()
            .Select(entry => entry.GetProperty("tenantId").GetGuid())
            .Distinct()
            .ToList();

        // The table holds both Samaaj; the global query filter is what keeps
        // one out of the other's trail.
        tenants.Should().ContainSingle().Which.Should().Be(TenantA);
    }

    [Fact]
    public async Task The_audit_log_can_be_filtered_by_action()
    {
        var logs = await AdminOf(TenantA)
            .GetFromJsonAsync<JsonElement>("/v1/audit/logs?action=UserRegistered");

        logs.EnumerateArray().Should().NotBeEmpty();
        logs.EnumerateArray().Select(e => e.GetProperty("action").GetString())
            .Should().AllBe("UserRegistered");
    }

    [Fact]
    public async Task A_filter_that_matches_nothing_returns_an_empty_list_rather_than_an_error()
    {
        var logs = await AdminOf(TenantA)
            .GetFromJsonAsync<JsonElement>("/v1/audit/logs?action=NoSuchAction");

        logs.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task An_out_of_range_limit_is_a_validation_problem()
    {
        var response = await AdminOf(TenantA).GetAsync("/v1/audit/logs?limit=5000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_member_sees_the_welcome_notification_the_consumer_raised_for_them()
    {
        var member = factory.CreateClientAs(MemberA, TenantA, ["Member"], []);

        var notifications = await member.GetFromJsonAsync<JsonElement>("/v1/notifications");

        notifications.EnumerateArray().Select(n => n.GetProperty("title").GetString())
            .Should().Contain("Welcome to your Samaaj");
    }

    [Fact]
    public async Task A_member_does_not_see_another_members_notifications()
    {
        var stranger = factory.CreateClientAs(Guid.NewGuid(), TenantA, ["Member"], []);

        var notifications = await stranger.GetFromJsonAsync<JsonElement>("/v1/notifications");

        notifications.EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task The_data_export_needs_a_token()
    {
        (await factory.CreateClient().GetAsync("/v1/audit/me/data-export"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_member_exports_their_notifications_and_what_they_are_used_for()
    {
        var member = factory.CreateClientAs(MemberA, TenantA, ["Member"], []);

        var export = await member.GetFromJsonAsync<JsonElement>("/v1/audit/me/data-export");

        export.GetProperty("notifications").EnumerateArray()
            .Select(n => n.GetProperty("title").GetString())
            .Should().Contain("Welcome to your Samaaj");

        export.GetProperty("processingPurposes").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task The_export_covers_actions_the_member_took_not_ones_about_them()
    {
        var member = factory.CreateClientAs(MemberA, TenantA, ["Member"], []);

        var export = await member.GetFromJsonAsync<JsonElement>("/v1/audit/me/data-export");

        // The seeded registration event names this member as the actor.
        export.GetProperty("actionsYouTook").EnumerateArray()
            .Select(a => a.GetProperty("action").GetString())
            .Should().Contain("UserRegistered");
    }

    [Fact]
    public async Task The_export_does_not_include_the_payload_of_what_changed()
    {
        var member = factory.CreateClientAs(MemberA, TenantA, ["Member"], []);

        var body = await member.GetStringAsync("/v1/audit/me/data-export");

        // The payload is the state of whatever was changed, which may be
        // someone else's data.
        body.Should().NotContain("afterState");
        body.Should().NotContain("beforeState");
    }

    [Fact]
    public async Task A_member_of_another_Samaaj_sees_none_of_this()
    {
        var stranger = factory.CreateClientAs(Guid.NewGuid(), TenantB, ["Member"], []);

        var export = await stranger.GetFromJsonAsync<JsonElement>("/v1/audit/me/data-export");

        export.GetProperty("notifications").EnumerateArray().Should().BeEmpty();
        export.GetProperty("actionsYouTook").EnumerateArray().Should().BeEmpty();
    }
}
