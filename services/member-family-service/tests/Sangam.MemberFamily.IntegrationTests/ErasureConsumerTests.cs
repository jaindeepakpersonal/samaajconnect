using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sangam.MemberFamily.Domain.Children;
using Sangam.MemberFamily.Domain.Families;
using Sangam.MemberFamily.Domain.Media;
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

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private sealed record Household(Guid HeadId, Guid FamilyId, Guid ChildId);

    private async Task<Household> SeedHouseholdAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var headId = Guid.NewGuid();
        var profile = MemberProfile.FromRegistration(
            headId, TenantId, "Ravi Shah", "ravi@example.com", DateTimeOffset.UtcNow);

        profile.Update(
            "Ravi Shah", new DateOnly(1985, 4, 2), Gender.Male,
            "9876543210", "ravi@example.com", "12 Temple Road", "Ghatkopar",
            "Chartered Accountant", FieldPrivacy.Default,
            isListedInDirectory: true, DateTimeOffset.UtcNow, Guid.NewGuid());

        db.MemberProfiles.Add(profile);

        var family = Family.Create(TenantId, headId, Family.GenerateCode(), DateTimeOffset.UtcNow);
        db.Families.Add(family);

        var child = ChildProfile.Create(
            TenantId, family.Id, "Aarav Shah", new DateOnly(2012, 7, 19),
            Gender.Male, headId, DateTimeOffset.UtcNow);

        db.ChildProfiles.Add(child);

        // Both get a photo, because the reference going null is not the same as
        // the photograph being gone - and the second is what erasure means.
        var headPhoto = StoredImage.Capture(
            TenantId, ImageOwnerKind.Member, headId, Png(), headId, DateTimeOffset.UtcNow);
        var childPhoto = StoredImage.Capture(
            TenantId, ImageOwnerKind.Child, child.Id, Png(), headId, DateTimeOffset.UtcNow);

        db.StoredImages.AddRange(headPhoto, childPhoto);
        profile.SetPhoto(headPhoto.Id, DateTimeOffset.UtcNow, headId);
        child.SetPhoto(childPhoto.Id);

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
        child.PhotoImageId.Should().BeNull();
        child.DateOfBirth.Should().Be(new DateOnly(2012, 1, 1));
    }

    /// <summary>
    /// The photographs, not just the references to them.
    /// </summary>
    /// <remarks>
    /// A row of bytes that nothing points at has not been erased; it is merely
    /// unreachable by the paths that happen to exist today. This asserts against
    /// the <c>stored_images</c> table rather than against the profile, and it
    /// runs the whole consumer rather than calling the store, because the thing
    /// that could quietly stop happening is the handler's call.
    /// </remarks>
    [Fact]
    public async Task An_erasure_event_deletes_the_photographs_themselves()
    {
        var household = await SeedHouseholdAsync();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var seeded = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

            (await seeded.StoredImages.IgnoreQueryFilters()
                .CountAsync(i => i.OwnerId == household.HeadId || i.OwnerId == household.ChildId))
                .Should().Be(2);
        }

        await PublishErasureAsync(household.HeadId);

        // The result is asserted, not just awaited. `EventuallyAsync` returns
        // the last value it saw when it times out rather than throwing, so an
        // `await` with no assertion after it passes however the wait ended -
        // which is exactly what this test did until removing the handler's call
        // failed to break it. The 44-second run was the only tell.
        var remaining = await factory.EventuallyAsync(
            db => db.StoredImages.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(i => i.OwnerId == household.HeadId || i.OwnerId == household.ChildId),
            found => found == 0);

        remaining.Should().Be(0);
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

    /// <summary>
    /// A household whose head erases keeps working for the people left in it.
    /// </summary>
    /// <remarks>
    /// Until this existed the household kept the erased member's id as its
    /// head, so `IsHead` was false for everybody and four things stopped at
    /// once: deciding a join request, adding a child, starting a conversion,
    /// and seeing the family code to invite anyone. Five people frozen because
    /// one of them exercised a right.
    ///
    /// This runs the whole consumer rather than calling the aggregate, because
    /// the thing that could quietly stop happening is the consumer's call.
    /// </remarks>
    [Fact]
    public async Task A_household_whose_head_erases_is_headed_by_whoever_is_left()
    {
        var household = await SeedHouseholdAsync();

        var survivor = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

            var family = await db.Families.IgnoreQueryFilters()
                .Include(f => f.Members)
                .SingleAsync(f => f.Id == household.FamilyId);

            var request = family.RequestJoin(
                survivor, Relationship.Sibling, DateTimeOffset.UtcNow.AddDays(-30))!;

            family.DecideJoinRequest(
                request.Id, accepted: true, household.HeadId, DateTimeOffset.UtcNow.AddDays(-30));

            await db.SaveChangesAsync();
        }

        await PublishErasureAsync(household.HeadId);

        var headed = await factory.EventuallyAsync(
            db => db.Families.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(f => f.Id == household.FamilyId),
            found => found.FamilyHeadMemberId == survivor);

        headed.FamilyHeadMemberId.Should().Be(survivor);
        headed.IsHead(survivor).Should().BeTrue();
    }
}
