using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Sangam.MemberFamily.Infrastructure.Messaging;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// The platform's first cross-service flow: a registration handled by
/// identity-tenant-service produces a profile here, over Kafka, with no
/// synchronous call between the two.
/// </summary>
public sealed class RegistrationConsumerTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private async Task<Guid> PublishRegistrationAsync(
        Guid userId,
        string fullName = "Ravi Shah",
        string identifier = "ravi@example.com",
        Guid? messageId = null,
        string? rawPayload = null)
    {
        var id = messageId ?? Guid.NewGuid();

        using var producer = factory.CreateProducer();

        var payload = rawPayload ?? $$"""
            {"userId":"{{userId}}","tenantId":"{{TenantId}}","mobileOrEmail":"{{identifier}}","fullName":"{{fullName}}"}
            """;

        await producer.ProduceAsync("identity.user.registered.v1", new Message<string, string>
        {
            Key = TenantId.ToString(),
            Value = payload,
            Headers =
            [
                new Header(EventHeaders.MessageId, Encoding.UTF8.GetBytes(id.ToString())),
                new Header(EventHeaders.EventType, Encoding.UTF8.GetBytes("UserRegisteredDomainEvent")),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(TenantId.ToString())),
                new Header(EventHeaders.OccurredAt, Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"))),
            ],
        });

        producer.Flush(TimeSpan.FromSeconds(10));

        return id;
    }

    [Fact]
    public async Task A_registration_elsewhere_creates_a_profile_here()
    {
        var userId = Guid.NewGuid();

        await PublishRegistrationAsync(userId, "Ravi Shah");

        var profile = await factory.EventuallyAsync(
            db => db.MemberProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == userId),
            found => found is not null);

        profile.Should().NotBeNull();
        profile!.FullName.Should().Be("Ravi Shah");
        profile.TenantId.Should().Be(TenantId);

        // The profile takes the user's own id, so the two services agree on one
        // identifier for a person without either owning the other's table.
        profile.Id.Should().Be(userId);
    }

    [Fact]
    public async Task The_registered_identifier_seeds_the_matching_contact_field()
    {
        var emailUser = Guid.NewGuid();
        var mobileUser = Guid.NewGuid();

        await PublishRegistrationAsync(emailUser, identifier: "meera@example.com");
        await PublishRegistrationAsync(mobileUser, identifier: "9812345678");

        var profiles = await factory.EventuallyAsync(
            db => db.MemberProfiles.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Id == emailUser || p.Id == mobileUser)
                .ToListAsync(),
            found => found.Count == 2);

        profiles.Single(p => p.Id == emailUser).Email.Should().Be("meera@example.com");
        profiles.Single(p => p.Id == mobileUser).Mobile.Should().Be("9812345678");
    }

    [Fact]
    public async Task A_new_profile_starts_with_contact_details_closed()
    {
        var userId = Guid.NewGuid();

        await PublishRegistrationAsync(userId);

        var profile = await factory.EventuallyAsync(
            db => db.MemberProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == userId),
            found => found is not null);

        profile!.Privacy.Email.Should().Be(Domain.Members.PrivacyLevel.Private);
        profile.Privacy.Mobile.Should().Be(Domain.Members.PrivacyLevel.SamaajOnly);
    }

    [Fact]
    public async Task The_same_registration_delivered_twice_produces_one_profile()
    {
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await PublishRegistrationAsync(userId, messageId: messageId);

        await factory.EventuallyAsync(
            db => db.MemberProfiles.IgnoreQueryFilters().CountAsync(p => p.Id == userId),
            count => count == 1);

        // At-least-once delivery makes a replay normal, not exceptional.
        await PublishRegistrationAsync(userId, messageId: messageId);
        await Task.Delay(2000);

        var profiles = await factory.WithDbContextAsync(db =>
            db.MemberProfiles.IgnoreQueryFilters().CountAsync(p => p.Id == userId));

        profiles.Should().Be(1);
    }

    [Fact]
    public async Task An_unusable_payload_is_skipped_rather_than_retried_forever()
    {
        // The consumer would otherwise exhaust its retries on this one message
        // and stall every registration queued behind it.
        await PublishRegistrationAsync(Guid.NewGuid(), rawPayload: "{\"nonsense\":true}");

        var goodUser = Guid.NewGuid();
        await PublishRegistrationAsync(goodUser, "Later Member");

        var profile = await factory.EventuallyAsync(
            db => db.MemberProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == goodUser),
            found => found is not null);

        profile.Should().NotBeNull();
    }
}
