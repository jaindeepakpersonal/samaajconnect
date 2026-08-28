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
/// The far end of the conversion loop: identity-tenant-service has created and
/// activated the account, and says so, and the child record catches up.
/// </summary>
public sealed class ConversionCompletionTests(MemberFamilyApiFactory factory)
    : IClassFixture<MemberFamilyApiFactory>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private async Task<Guid> SeedChildAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemberFamilyDbContext>();

        var headId = Guid.NewGuid();

        db.MemberProfiles.Add(MemberProfile.FromRegistration(
            headId, TenantId, "Ravi Shah", "ravi@example.com", DateTimeOffset.UtcNow));

        var family = Family.Create(TenantId, headId, Family.GenerateCode(), DateTimeOffset.UtcNow);
        db.Families.Add(family);

        var child = ChildProfile.Create(
            TenantId,
            family.Id,
            "Aarav Jain",
            DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-19),
            Gender.Male,
            null,
            DateTimeOffset.UtcNow);

        db.ChildProfiles.Add(child);

        await db.SaveChangesAsync();

        return child.Id;
    }

    private async Task PublishCompletionAsync(Guid childProfileId, Guid userId, Guid? messageId = null)
    {
        using var producer = factory.CreateProducer();

        await producer.ProduceAsync("identity.child-conversion.completed.v1", new Message<string, string>
        {
            Key = TenantId.ToString(),
            Value = $$"""
                {"userId":"{{userId}}","tenantId":"{{TenantId}}","childProfileId":"{{childProfileId}}"}
                """,
            Headers =
            [
                new Header(EventHeaders.MessageId,
                    Encoding.UTF8.GetBytes((messageId ?? Guid.NewGuid()).ToString())),
                new Header(EventHeaders.EventType,
                    Encoding.UTF8.GetBytes("UserActivatedFromChildDomainEvent")),
                new Header(EventHeaders.TenantId, Encoding.UTF8.GetBytes(TenantId.ToString())),
                new Header(EventHeaders.OccurredAt,
                    Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"))),
            ],
        });

        producer.Flush(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task An_activated_account_marks_the_child_converted_and_links_the_two()
    {
        var childId = await SeedChildAsync();
        var userId = Guid.NewGuid();

        await PublishCompletionAsync(childId, userId);

        var child = await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == childId),
            found => found is not null && found.Status == ChildStatus.Converted);

        child.Should().NotBeNull();
        child!.Status.Should().Be(ChildStatus.Converted);
        child.ConvertedMemberId.Should().Be(userId);
    }

    [Fact]
    public async Task A_converted_child_is_no_longer_eligible_for_conversion()
    {
        var childId = await SeedChildAsync();

        await PublishCompletionAsync(childId, Guid.NewGuid());

        var child = await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == childId),
            found => found?.Status == ChildStatus.Converted);

        child!.IsEligibleForConversion(DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeFalse();
    }

    [Fact]
    public async Task The_same_completion_delivered_twice_changes_nothing_the_second_time()
    {
        var childId = await SeedChildAsync();
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await PublishCompletionAsync(childId, userId, messageId);

        await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == childId),
            found => found?.Status == ChildStatus.Converted);

        // At-least-once makes a replay normal, and a second one must not
        // re-link the child to some other account.
        await PublishCompletionAsync(childId, Guid.NewGuid(), messageId);
        await Task.Delay(2000);

        var child = await factory.WithDbContextAsync(db =>
            db.ChildProfiles.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == childId));

        child.ConvertedMemberId.Should().Be(userId);
    }

    [Fact]
    public async Task A_completion_for_a_child_that_no_longer_exists_is_ignored()
    {
        // Must not stall the partition behind it.
        await PublishCompletionAsync(Guid.NewGuid(), Guid.NewGuid());

        var childId = await SeedChildAsync();
        await PublishCompletionAsync(childId, Guid.NewGuid());

        var child = await factory.EventuallyAsync(
            db => db.ChildProfiles.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == childId),
            found => found?.Status == ChildStatus.Converted);

        child.Should().NotBeNull();
    }
}
