using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Domain.Members;
using Sangam.MemberFamily.Infrastructure.Messaging;
using Sangam.MemberFamily.Infrastructure.Persistence;
using Xunit;

namespace Sangam.MemberFamily.IntegrationTests;

/// <summary>
/// A member erased their account in identity-tenant-service; everything this
/// service holds about them has to follow.
/// </summary>
/// <remarks>
/// Real Kafka and real Postgres because the failure this catches is not in the
/// handler. A consumer has no request and therefore no resolved tenant, so
/// every lookup on this path has to see past the global query filter - and one
/// that does not finds nothing, reports success, and leaves the data in place.
/// That has already happened twice on other consumers here.
/// </remarks>
public sealed class ErasureConsumerTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private sealed record Household(Guid HeadId, Guid FamilyId, Guid ChildId);

    private async Task<Household> SeedHouseholdAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var headId = Guid.NewGuid();
        var profile = MemberProfile.FromRegistration(
            headId, TenantId, "Ravi Shah", "ravi@example.com", DateTimeOffset.UtcNow);

        profile.Update(
            "Ravi Shah", null, new DateOnly(1985, 4, 2), Gender.Male,
            "9876543210", "ravi@example.com", "12 Temple Road", "Ghatkopar",
            "Chartered Accountant", FieldPrivacy.Default, DateTimeOffset.UtcNow);

        db.MemberProfiles.Add(profile);

        var family = Family.Create(TenantId, headId, Family.GenerateCode(), DateTimeOffset.UtcNow);
        db.Families.Add(family);

        var child = ChildProfile.Create(
            TenantId, family.Id, "Aarav Shah", new DateOnly(2012, 7, 19),
            Gender.Male, "https://cdn/aarav.jpg", headId, DateTimeOffset.UtcNow);

        db.ChildProfiles.Add(child);

        await db.SaveChangesAsync();

        return new Household(headId, family.Id, child.Id);
    }

    private async Task PublishErasureAsync(Guid userId, Guid? messageId = null)
    {
        using var producer = factory.CreateProducer();

        await producer.ProduceAsync("identity.user.erased.v1", new Message<string, string>
        {
            Key = TenantId.ToString(),
            Value = $$"""{"userId":"{{userId}}","tenantId":"{{TenantId}}"}""",
            Headers =
            [
                new Header(EventHeaders.MessageId,
                    Encoding.UTF8.GetBytes((messageId ?? Guid.NewGuid()).ToString())),
                new Header(EventHeaders.EventType,
                    Encoding.UTF8.GetBytes("UserErasedDomainEvent")),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(TenantId.ToString())),
                new Header(EventHeaders.OccurredAt,
                    Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"))),
            ],
        });

        producer.Flush(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task An_erasure_event_clears_the_profile()
    {
        var household = await SeedHouseholdAsync();

        await PublishErasureAsync(household.HeadId);

        var profile = await factory.EventuallyAsync(
            db => db.MemberProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(p => p.Id == household.HeadId),
            found => found.Mobile is null);

        profile.Mobile.Should().BeNull();
        profile.Email.Should().BeNull();
        profile.Address.Should().BeNull();
        profile.Profession.Should().BeNull();
        profile.DateOfBirth.Should().BeNull();
        profile.FullName.Should().NotContain("Ravi");
    }

    [Fact]
    public async Task An_erasure_event_erases_the_children_that_member_headed()
    {
        // Those records were held on this person's parental consent, and the
        // consent no longer exists.
        var household = await SeedHouseholdAsync();

        await PublishErasureAsync(household.HeadId);

        var child = await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(c => c.Id == household.ChildId),
            found => !found.FullName.Contains("Aarav"));

        child.FullName.Should().NotContain("Aarav");
        child.PhotoUrl.Should().BeNull();
        child.DateOfBirth.Should().Be(new DateOnly(2012, 1, 1));
    }

    [Fact]
    public async Task An_erasure_event_removes_the_household_link_but_not_the_household()
    {
        var household = await SeedHouseholdAsync();

        await PublishErasureAsync(household.HeadId);

        await factory.EventuallyAsync(
            db => db.FamilyMembers.IgnoreQueryFilters()
                .CountAsync(m => m.MemberProfileId == household.HeadId),
            count => count == 0);

        var family = await factory.WithDbContextAsync(db =>
            db.Families.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(f => f.Id == household.FamilyId));

        // Other members joined this household. One person exercising their own
        // right must not delete everyone else's.
        family.Should().NotBeNull();
    }

    [Fact]
    public async Task An_erasure_for_someone_else_leaves_this_member_alone()
    {
        var household = await SeedHouseholdAsync();

        await PublishErasureAsync(Guid.NewGuid());

        await Task.Delay(3000);

        var profile = await factory.WithDbContextAsync(db =>
            db.MemberProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(p => p.Id == household.HeadId));

        profile.FullName.Should().Be("Ravi Shah");
    }

    [Fact]
    public async Task The_same_erasure_delivered_twice_is_harmless()
    {
        // At-least-once delivery guarantees this happens; the second pass finds
        // an already-erased profile and must not fail the message.
        var household = await SeedHouseholdAsync();
        var messageId = Guid.NewGuid();

        await PublishErasureAsync(household.HeadId, messageId);

        await factory.EventuallyAsync(
            db => db.MemberProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(p => p.Id == household.HeadId),
            found => found.Mobile is null);

        await PublishErasureAsync(household.HeadId, messageId);

        await Task.Delay(2000);

        var profiles = await factory.WithDbContextAsync(db =>
            db.MemberProfiles.IgnoreQueryFilters().CountAsync(p => p.Id == household.HeadId));

        profiles.Should().Be(1);
    }
}
