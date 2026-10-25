using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
/// The far end of the conversion loop: identity-tenant-service has created and
/// activated the account, and says so, and the child record catches up - and
/// the new adult member gets a profile and a place in their household.
/// </summary>
public sealed class ConversionCompletionTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CityId = Guid.NewGuid();
    private static readonly Guid LocalityId = Guid.NewGuid();
    private static readonly DateOnly ChildDateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-19);

    private sealed record Seeded(Guid ChildId, Guid FamilyId, Guid AdminId);

    private async Task<Seeded> SeedChildAsync(
        ChildStatus status = ChildStatus.Minor, Guid? convertedMemberId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var adminId = Guid.NewGuid();

        var admin = MemberProfile.FromRegistration(
            adminId, TenantId, "Ravi Shah", "ravi@example.com", DateTimeOffset.UtcNow);

        admin.Update(
            "Ravi Shah",
            null,
            Gender.Male,
            null,
            "ravi@example.com",
            null,
            null,
            null,
            new ProfileAddress("12 Hiran Magri", CityId, LocalityId),
            null,
            FieldPrivacy.Default,
            true,
            DateTimeOffset.UtcNow,
            adminId);

        db.MemberProfiles.Add(admin);

        var family = Family.Create(TenantId, adminId, Family.GenerateCode(), DateTimeOffset.UtcNow);
        db.Families.Add(family);

        var child = ChildProfile.Create(
            TenantId,
            family.Id,
            "Aarav Jain",
            ChildDateOfBirth,
            Gender.Male,
            adminId,
            DateTimeOffset.UtcNow);

        if (status == ChildStatus.Converted)
        {
            child.MarkConverted(convertedMemberId ?? Guid.NewGuid());
        }

        db.ChildProfiles.Add(child);

        await db.SaveChangesAsync();

        return new Seeded(child.Id, family.Id, adminId);
    }

    private async Task PublishCompletionAsync(
        Guid childProfileId,
        Guid userId,
        Guid? messageId = null,
        Guid? tenantId = null,
        string mobileOrEmail = "aarav@example.com")
    {
        var tenant = tenantId ?? TenantId;

        using var producer = factory.CreateProducer();

        await producer.ProduceAsync("identity.child-conversion.completed.v1", new Message<string, string>
        {
            // Always the seeded Samaaj, so every message here shares a partition
            // and a later one is a reliable sign the earlier one was handled.
            Key = TenantId.ToString(),
            Value = $$"""
                {"userId":"{{userId}}","tenantId":"{{tenant}}","childProfileId":"{{childProfileId}}","mobileOrEmail":"{{mobileOrEmail}}"}
                """,
            Headers =
            [
                new Header(EventHeaders.MessageId,
                    Encoding.UTF8.GetBytes((messageId ?? Guid.NewGuid()).ToString())),
                new Header(EventHeaders.EventType,
                    Encoding.UTF8.GetBytes("UserActivatedFromChildDomainEvent")),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(tenant.ToString())),
                new Header(EventHeaders.OccurredAt,
                    Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"))),
            ],
        });

        producer.Flush(TimeSpan.FromSeconds(10));
    }

    private Task<MemberProfile?> EventuallyProfileAsync(Guid userId) =>
        factory.EventuallyAsync(
            db => db.MemberProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == userId),
            found => found is not null);

    [Fact]
    public async Task An_activated_account_marks_the_child_converted_and_links_the_two()
    {
        var seeded = await SeedChildAsync();
        var userId = Guid.NewGuid();

        await PublishCompletionAsync(seeded.ChildId, userId);

        var child = await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == seeded.ChildId),
            found => found is not null && found.Status == ChildStatus.Converted);

        child.Should().NotBeNull();
        child!.Status.Should().Be(ChildStatus.Converted);
        child.ConvertedMemberId.Should().Be(userId);
    }

    [Fact]
    public async Task A_converted_child_is_no_longer_eligible_for_conversion()
    {
        var seeded = await SeedChildAsync();

        await PublishCompletionAsync(seeded.ChildId, Guid.NewGuid());

        var child = await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == seeded.ChildId),
            found => found?.Status == ChildStatus.Converted);

        child!.IsEligibleForConversion(DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeFalse();
    }

    [Fact]
    public async Task The_new_member_gets_a_profile_seeded_from_the_child_record()
    {
        var seeded = await SeedChildAsync();
        var userId = Guid.NewGuid();

        await PublishCompletionAsync(seeded.ChildId, userId, mobileOrEmail: "Aarav@Example.com");

        var profile = await EventuallyProfileAsync(userId);

        profile.Should().NotBeNull("a converted child signs in as a Member and needs a profile");
        profile!.TenantId.Should().Be(TenantId);
        profile.FullName.Should().Be("Aarav Jain");
        profile.DateOfBirth.Should().Be(ChildDateOfBirth);
        profile.Gender.Should().Be(Gender.Male);
        profile.Email.Should().Be("aarav@example.com");
        profile.Mobile.Should().BeNull();
        profile.Privacy.Should().Be(FieldPrivacy.Default);
        profile.IsListedInDirectory.Should().BeTrue();
        profile.PhotoImageId.Should().BeNull("the child's photograph was held on a parent's consent");
        profile.MaritalStatus.Should().BeNull();
    }

    [Fact]
    public async Task The_new_profile_takes_the_family_admins_permanent_address()
    {
        var seeded = await SeedChildAsync();
        var userId = Guid.NewGuid();

        await PublishCompletionAsync(seeded.ChildId, userId);

        var profile = await EventuallyProfileAsync(userId);

        profile!.PermanentAddress.Should().Be(new ProfileAddress("12 Hiran Magri", CityId, LocalityId));
        profile.TemporaryAddress.Should().BeNull();
        profile.Privacy.Address.Should().Be(PrivacyLevel.Private);
    }

    [Fact]
    public async Task The_new_member_joins_their_household_as_an_active_child()
    {
        var seeded = await SeedChildAsync();
        var userId = Guid.NewGuid();

        await PublishCompletionAsync(seeded.ChildId, userId);

        var family = await factory.EventuallyAsync(
            db => db.Families.IgnoreQueryFilters().AsNoTracking().Include(f => f.Members)
                .SingleAsync(f => f.Id == seeded.FamilyId),
            found => found.Members.Any(m => m.MemberProfileId == userId));

        var membership = family.Members.Single(m => m.MemberProfileId == userId);

        membership.Status.Should().Be(FamilyMemberStatus.Active);
        membership.Relationship.Should().Be(Relationship.Child);
        family.FamilyAdminMemberId.Should().Be(seeded.AdminId, "joining hands nobody the admin role");
    }

    [Fact]
    public async Task The_new_member_can_read_their_own_profile_and_household()
    {
        var seeded = await SeedChildAsync();
        var userId = Guid.NewGuid();

        await PublishCompletionAsync(seeded.ChildId, userId);
        await EventuallyProfileAsync(userId);

        var client = factory.CreateClientAs(userId, TenantId, ["Member"], ["Members.Read"]);

        var meResponse = await client.GetAsync("/v1/members/me");
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("id").GetGuid().Should().Be(userId);
        me.GetProperty("fullName").GetString().Should().Be("Aarav Jain");

        var family = await client.GetFromJsonAsync<JsonElement>("/v1/families/mine");
        family.GetProperty("id").GetGuid().Should().Be(seeded.FamilyId);
    }

    [Fact]
    public async Task The_same_completion_delivered_twice_changes_nothing_the_second_time()
    {
        var seeded = await SeedChildAsync();
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await PublishCompletionAsync(seeded.ChildId, userId, messageId);

        await EventuallyProfileAsync(userId);

        // At-least-once makes a replay normal, and a second one must not
        // re-link the child to some other account, nor create a second profile.
        var otherUserId = Guid.NewGuid();
        await PublishCompletionAsync(seeded.ChildId, otherUserId, messageId);
        await PublishCompletionAsync(seeded.ChildId, userId);
        await Task.Delay(2000);

        var child = await factory.WithDbContextAsync(db =>
            db.ChildProfiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == seeded.ChildId));

        child.ConvertedMemberId.Should().Be(userId);

        (await factory.WithDbContextAsync(db =>
                db.MemberProfiles.IgnoreQueryFilters().AnyAsync(p => p.Id == otherUserId)))
            .Should().BeFalse();

        var family = await factory.WithDbContextAsync(db =>
            db.Families.IgnoreQueryFilters().AsNoTracking().Include(f => f.Members)
                .SingleAsync(f => f.Id == seeded.FamilyId));

        family.Members.Count(m => m.MemberProfileId == userId).Should().Be(1);
    }

    [Fact]
    public async Task A_member_who_left_their_household_is_not_put_back_by_a_redelivery()
    {
        var seeded = await SeedChildAsync();
        var userId = Guid.NewGuid();

        await PublishCompletionAsync(seeded.ChildId, userId);
        await EventuallyProfileAsync(userId);

        var leave = await factory.CreateClientAs(userId, TenantId, ["Member"], ["Members.Read"])
            .DeleteAsync("/v1/families/mine/membership");
        leave.IsSuccessStatusCode.Should().BeTrue();

        await PublishCompletionAsync(seeded.ChildId, userId);
        await Task.Delay(2000);

        var family = await factory.WithDbContextAsync(db =>
            db.Families.IgnoreQueryFilters().AsNoTracking().Include(f => f.Members)
                .SingleAsync(f => f.Id == seeded.FamilyId));

        family.Members.Should().NotContain(m => m.MemberProfileId == userId);
    }

    [Fact]
    public async Task A_child_converted_before_profiles_were_created_gets_one_on_redelivery()
    {
        // Every conversion completed before this consumer created profiles left
        // an account with no profile. Replaying the event repairs it.
        var userId = Guid.NewGuid();
        var seeded = await SeedChildAsync(ChildStatus.Converted, userId);

        await PublishCompletionAsync(seeded.ChildId, userId);

        var profile = await EventuallyProfileAsync(userId);

        profile.Should().NotBeNull();
        profile!.FullName.Should().Be("Aarav Jain");
    }

    [Fact]
    public async Task A_completion_naming_another_samaaj_is_ignored()
    {
        var seeded = await SeedChildAsync();
        var userId = Guid.NewGuid();

        await PublishCompletionAsync(seeded.ChildId, userId, tenantId: Guid.NewGuid());

        // Something after it on the same topic is processed, so the first one
        // was handled - and ignored - rather than still in flight.
        var marker = await SeedChildAsync();
        await PublishCompletionAsync(marker.ChildId, Guid.NewGuid());
        await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == marker.ChildId),
            found => found?.Status == ChildStatus.Converted);

        var child = await factory.WithDbContextAsync(db =>
            db.ChildProfiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == seeded.ChildId));

        child.Status.Should().Be(ChildStatus.Minor);

        (await factory.WithDbContextAsync(db =>
                db.MemberProfiles.IgnoreQueryFilters().AnyAsync(p => p.Id == userId)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_completion_for_a_child_that_no_longer_exists_is_ignored()
    {
        // Must not stall the partition behind it.
        await PublishCompletionAsync(Guid.NewGuid(), Guid.NewGuid());

        var seeded = await SeedChildAsync();
        await PublishCompletionAsync(seeded.ChildId, Guid.NewGuid());

        var child = await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == seeded.ChildId),
            found => found?.Status == ChildStatus.Converted);

        child.Should().NotBeNull();
    }
}
